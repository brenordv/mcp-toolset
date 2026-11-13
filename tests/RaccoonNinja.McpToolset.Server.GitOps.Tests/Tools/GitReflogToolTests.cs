using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Server.GitOps.Metrics;
using RaccoonNinja.McpToolset.Server.GitOps.Models;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;
using RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;
using RaccoonNinja.McpToolset.Server.GitOps.Tools;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Tools;

public sealed class GitReflogToolTests : IDisposable
{
    private const string TrackedFile = "a.txt";
    private const string SignedSubject = "signed";
    private const string ReflogSubcommand = "reflog";
    private const int KeptBytesInsideTheHash = 20;
    private const int KeptBytesInsideTheShortHash = 45;
    private const int KeptBytesInsideTheSelector = 53;
    private const int KeptBytesInsideTheSubject = 64;
    private const string CutSubject = "commit";

    private readonly TempGitRepo _repo;
    private readonly GitReflogTool _tool;

    public GitReflogToolTests()
    {
        _repo = new TempGitRepo();
        _repo.WriteFile(TrackedFile, "one\n");
        _repo.Run("add", ".");
        _repo.Run("commit", "-m", "first");

        _tool = BuildTool(new GitProcessRunner(NullLogger<GitProcessRunner>.Instance));
    }

    public void Dispose() => _repo.Dispose();

    [Fact]
    public async Task Reflog_WithShowSignatureConfigAndSignedCommit_DoesNotRunSignatureProgram()
    {
        // Arrange
        var signedHash = _repo.CommitWithForgedSignature(SignedSubject);
        var marker = _repo.ConfigureSignatureProgramTrap();

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, maxCount: 1, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var entry = Assert.IsType<ReflogEntry>(Assert.Single(envelope.Results));
        Assert.Equal(signedHash, entry.Hash);
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task Reflog_SubjectHoldingFieldSeparator_ReportsUnshiftedEntry()
    {
        // Arrange
        var authorTime = _repo.CommitWithReflogSubject(TempGitRepo.ShiftingSubject);

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, maxCount: 1, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var entry = Assert.IsType<ReflogEntry>(Assert.Single(envelope.Results));
        Assert.Equal($"commit: {TempGitRepo.ShiftingSubject}", entry.Subject);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(authorTime), entry.When);
    }

    [Theory]
    [InlineData(KeptBytesInsideTheHash)]
    [InlineData(KeptBytesInsideTheShortHash)]
    [InlineData(KeptBytesInsideTheSelector)]
    public async Task Reflog_OutputCutInsideAnIdentifyingField_LeavesTheEntryOut(int keptBytes)
    {
        // Arrange
        var tool = BuildTool(new OutputCuttingRunner(argv => argv.Contains(ReflogSubcommand), keptBytes));

        // Act
        var envelope = await tool.InvokeAsync(_repo.RepoPath, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.True(envelope.Truncated);
        Assert.Empty(envelope.Results);
    }

    [Fact]
    public async Task Reflog_OutputCutInsideTheSubject_ReturnsTheCutEntryMarkedTruncated()
    {
        // Arrange
        var head = _repo.Run("rev-parse", "HEAD");
        var tool = BuildTool(new OutputCuttingRunner(argv => argv.Contains(ReflogSubcommand), KeptBytesInsideTheSubject));

        // Act
        var envelope = await tool.InvokeAsync(_repo.RepoPath, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.True(envelope.Truncated);
        var entry = Assert.IsType<ReflogEntry>(Assert.Single(envelope.Results));
        Assert.Equal(head, entry.Hash);
        Assert.Equal(CutSubject, entry.Subject);
    }

    #region Test Helpers

    /// <summary>Build the tool under test over <paramref name="runner"/>, with the real root resolver.</summary>
    /// <param name="runner">The runner every git process of the call goes through.</param>
    /// <returns>The tool.</returns>
    private static GitReflogTool BuildTool(IGitProcessRunner runner)
    {
        var common = new ToolCommon(
            new RepoRootResolver(TestGit.Executable),
            runner,
            new SessionMetrics(),
            NullLoggerFactory.Instance,
            TestGit.Executable);
        return new GitReflogTool(common);
    }

    #endregion
}