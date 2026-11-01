using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Server.GitOps.Metrics;
using RaccoonNinja.McpToolset.Server.GitOps.Models;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;
using RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;
using RaccoonNinja.McpToolset.Server.GitOps.Tools;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Tools;

public sealed class GitStashListToolTests : IDisposable
{
    private const string TrackedFile = "a.txt";
    private const string SignedSubject = "signed";
    private const string StashRef = "refs/stash";
    private const string StashSubcommand = "stash";
    private const string StashSubject = "stash subject";
    private const int KeptEntryBytes = 12;
    private const string CutStashSubject = "st";

    private readonly TempGitRepo _repo;
    private readonly GitStashListTool _tool;

    public GitStashListToolTests()
    {
        _repo = new TempGitRepo();
        _repo.WriteFile(TrackedFile, "one\n");
        _repo.Run("add", ".");
        _repo.Run("commit", "-m", "first");

        _tool = BuildTool(new GitProcessRunner(NullLogger<GitProcessRunner>.Instance));
    }

    public void Dispose() => _repo.Dispose();

    [Fact]
    public async Task StashList_WithShowSignatureConfigAndSignedStashCommit_DoesNotRunSignatureProgram()
    {
        // Arrange
        var signedHash = _repo.CommitWithForgedSignature(SignedSubject);
        _repo.Run("update-ref", "--create-reflog", "-m", SignedSubject, StashRef, signedHash);
        var marker = _repo.ConfigureSignatureProgramTrap();

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var entry = Assert.IsType<StashEntry>(Assert.Single(envelope.Results));
        Assert.Equal(SignedSubject, entry.Subject);
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task StashList_SubjectHoldingFieldSeparator_ReportsUnshiftedEntry()
    {
        // Arrange
        var head = _repo.Run("rev-parse", "HEAD");
        _repo.Run("update-ref", "--create-reflog", "-m", TempGitRepo.ShiftingSubject, StashRef, head);

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var entry = Assert.IsType<StashEntry>(Assert.Single(envelope.Results));
        Assert.Equal(0, entry.Index);
        Assert.Equal(TempGitRepo.ShiftingSubject, entry.Subject);
    }

    [Fact]
    public async Task StashList_OutputCut_ReturnsTheCutEntryMarkedTruncated()
    {
        // Arrange
        var head = _repo.Run("rev-parse", "HEAD");
        _repo.Run("update-ref", "--create-reflog", "-m", StashSubject, StashRef, head);
        var tool = BuildTool(new OutputCuttingRunner(argv => argv.Contains(StashSubcommand), KeptEntryBytes));

        // Act
        var envelope = await tool.InvokeAsync(_repo.RepoPath, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.True(envelope.Truncated);
        var entry = Assert.IsType<StashEntry>(Assert.Single(envelope.Results));
        Assert.Equal(0, entry.Index);
        Assert.Equal(CutStashSubject, entry.Subject);
    }

    #region Test Helpers

    /// <summary>Build the tool under test over <paramref name="runner"/>, with the real root resolver.</summary>
    /// <param name="runner">The runner every git process of the call goes through.</param>
    /// <returns>The tool.</returns>
    private static GitStashListTool BuildTool(IGitProcessRunner runner)
    {
        var common = new ToolCommon(
            new RepoRootResolver(TestGit.Executable),
            runner,
            new SessionMetrics(),
            NullLoggerFactory.Instance,
            TestGit.Executable);
        return new GitStashListTool(common);
    }

    #endregion
}