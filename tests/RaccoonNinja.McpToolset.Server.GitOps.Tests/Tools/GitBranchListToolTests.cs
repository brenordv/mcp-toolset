using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Server.GitOps.Metrics;
using RaccoonNinja.McpToolset.Server.GitOps.Models;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;
using RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;
using RaccoonNinja.McpToolset.Server.GitOps.Tools;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Tools;

public sealed class GitBranchListToolTests : IDisposable
{
    private const string TrackedFile = "a.txt";
    private const string FirstSubject = "first";
    private const string MainBranch = "main";
    private const string OtherBranch = "other";
    private const string LocalUpstream = "origin/main";
    private const string ForEachRefSubcommand = "for-each-ref";
    private const string ForgedTipHash = "fedcba9876543210fedcba9876543210fedcba98";
    private const string ForgedUpstream =
        LocalUpstream + "\u001fspoofed subject\nrefs/heads/forged\u001f \u001f" + ForgedTipHash + "\u001f\u001fforged subject";
    private const int KeptBytesInsideTheSubject = 63;
    private const string CutSubject = "fir";
    private const int KeptBytesInsideTheSecondRefname = 80;
    private const int KeptBytesInsideTheSecondObjectName = 96;
    private const int KeptBytesInsideTheUpstream = 62;
    private const int KeptBytesBeforeTheSubject = 60;
    private const int KeptBytesBeforeTheNewline = 66;
    private const int KeptEveryByte = int.MaxValue;

    private readonly TempGitRepo _repo;
    private readonly GitBranchListTool _tool;

    public GitBranchListToolTests()
    {
        _repo = new TempGitRepo();
        _repo.WriteFile(TrackedFile, "one\n");
        _repo.Run("add", ".");
        _repo.Run("commit", "-m", FirstSubject);

        _tool = BuildTool(new GitProcessRunner(NullLogger<GitProcessRunner>.Instance));
    }

    public void Dispose() => _repo.Dispose();

    [Fact]
    public async Task BranchList_TipSubjectHoldingUnitSeparator_ReturnsTheWholeSubject()
    {
        // Arrange
        _repo.CommitWithReflogSubject(TempGitRepo.ShiftingSubject);

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var branch = Assert.IsType<Branch>(Assert.Single(envelope.Results));
        Assert.Equal(MainBranch, branch.Name);
        Assert.Equal(TempGitRepo.ShiftingSubject, branch.Subject);
    }

    [Fact]
    public async Task BranchList_UpstreamHoldingNewlineAndUnitSeparators_ReturnsOnlyTheRealBranch()
    {
        // Arrange
        var head = _repo.Run("rev-parse", "HEAD");
        _repo.ConfigureLocalUpstream(ForgedUpstream);

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var branch = Assert.IsType<Branch>(Assert.Single(envelope.Results));
        Assert.Equal(MainBranch, branch.Name);
        Assert.True(branch.IsCurrent);
        Assert.Equal(head, branch.TipHash);
        Assert.Equal(ForgedUpstream, branch.Upstream);
        Assert.Equal(FirstSubject, branch.Subject);
    }

    [Fact]
    public async Task BranchList_OutputCutInsideTheSubject_ReturnsTheCutEntryMarkedTruncated()
    {
        // Arrange
        var head = _repo.Run("rev-parse", "HEAD");
        var tool = BuildTool(new OutputCuttingRunner(argv => argv.Contains(ForEachRefSubcommand), KeptBytesInsideTheSubject));

        // Act
        var envelope = await tool.InvokeAsync(_repo.RepoPath, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.True(envelope.Truncated);
        var branch = Assert.IsType<Branch>(Assert.Single(envelope.Results));
        Assert.Equal(MainBranch, branch.Name);
        Assert.Equal(head, branch.TipHash);
        Assert.Equal(CutSubject, branch.Subject);
    }

    [Fact]
    public async Task BranchList_OutputCutInsideTheSecondRefname_ReturnsOnlyTheWholeEntry()
    {
        // Arrange
        _repo.Run("branch", OtherBranch);
        var tool = BuildTool(new OutputCuttingRunner(argv => argv.Contains(ForEachRefSubcommand), KeptBytesInsideTheSecondRefname));

        // Act
        var envelope = await tool.InvokeAsync(_repo.RepoPath, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.True(envelope.Truncated);
        var branch = Assert.IsType<Branch>(Assert.Single(envelope.Results));
        Assert.Equal(MainBranch, branch.Name);
    }

    [Fact]
    public async Task BranchList_OutputCutInsideTheSecondObjectName_ReturnsOnlyTheWholeEntry()
    {
        // Arrange
        _repo.Run("branch", OtherBranch);
        var tool = BuildTool(new OutputCuttingRunner(argv => argv.Contains(ForEachRefSubcommand), KeptBytesInsideTheSecondObjectName));

        // Act
        var envelope = await tool.InvokeAsync(_repo.RepoPath, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.True(envelope.Truncated);
        var branch = Assert.IsType<Branch>(Assert.Single(envelope.Results));
        Assert.Equal(MainBranch, branch.Name);
    }

    [Fact]
    public async Task BranchList_OutputCutInsideTheUpstream_LeavesTheEntryOut()
    {
        // Arrange
        _repo.ConfigureLocalUpstream(LocalUpstream);
        var tool = BuildTool(new OutputCuttingRunner(argv => argv.Contains(ForEachRefSubcommand), KeptBytesInsideTheUpstream));

        // Act
        var envelope = await tool.InvokeAsync(_repo.RepoPath, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.True(envelope.Truncated);
        Assert.Empty(envelope.Results);
    }

    [Fact]
    public async Task BranchList_OutputCutRightBeforeTheSubject_LeavesTheEntryOut()
    {
        // Arrange
        var tool = BuildTool(new OutputCuttingRunner(argv => argv.Contains(ForEachRefSubcommand), KeptBytesBeforeTheSubject));

        // Act
        var envelope = await tool.InvokeAsync(_repo.RepoPath, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.True(envelope.Truncated);
        Assert.Empty(envelope.Results);
    }

    [Fact]
    public async Task BranchList_OutputCutRightBeforeTheNewline_ReturnsTheWholeEntry()
    {
        // Arrange
        var head = _repo.Run("rev-parse", "HEAD");
        var tool = BuildTool(new OutputCuttingRunner(argv => argv.Contains(ForEachRefSubcommand), KeptBytesBeforeTheNewline));

        // Act
        var envelope = await tool.InvokeAsync(_repo.RepoPath, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.True(envelope.Truncated);
        var branch = Assert.IsType<Branch>(Assert.Single(envelope.Results));
        Assert.Equal(MainBranch, branch.Name);
        Assert.Equal(head, branch.TipHash);
        Assert.Equal(FirstSubject, branch.Subject);
    }

    [Fact]
    public async Task BranchList_TruncatedReplyEndingOnAnEmptySubject_LeavesTheEntryOut()
    {
        // Arrange
        _repo.Run("commit", "--allow-empty", "--allow-empty-message", "-m", string.Empty);
        var tool = BuildTool(new OutputCuttingRunner(argv => argv.Contains(ForEachRefSubcommand), KeptEveryByte));

        // Act
        var envelope = await tool.InvokeAsync(_repo.RepoPath, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.True(envelope.Truncated);
        Assert.Empty(envelope.Results);
    }

    #region Test Helpers

    /// <summary>Build the tool under test over <paramref name="runner"/>, with the real root resolver.</summary>
    /// <param name="runner">The runner every git process of the call goes through.</param>
    /// <returns>The tool.</returns>
    private static GitBranchListTool BuildTool(IGitProcessRunner runner)
    {
        var common = new ToolCommon(
            new RepoRootResolver(TestGit.Executable),
            runner,
            new SessionMetrics(),
            NullLoggerFactory.Instance,
            TestGit.Executable);
        return new GitBranchListTool(common);
    }

    #endregion
}