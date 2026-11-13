using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Server.GitOps.Metrics;
using RaccoonNinja.McpToolset.Server.GitOps.Models;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;
using RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;
using RaccoonNinja.McpToolset.Server.GitOps.Tools;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Tools;

public sealed class GitStatusToolTests : IDisposable
{
    private const string TrackedFile = "a.txt";
    private const string SubmodulePath = "sub";
    private const string FirstUntrackedFile = "u1.txt";
    private const string SecondUntrackedFile = "u2.txt";
    private const string RenamedFile = "c.txt";
    private const string StatusSubcommand = "status";
    private const int KeptBytesInsideTheSecondUntracked = 86;
    private const int KeptBytesAfterTheRenamedPath = 197;
    private const int KeptBytesInsideTheOriginalPath = 200;
    private const int KeptEveryStatusByte = 203;

    private readonly TempGitRepo _repo;
    private readonly GitStatusTool _tool;

    public GitStatusToolTests()
    {
        _repo = new TempGitRepo();
        _repo.WriteFile(TrackedFile, "one\n");
        _repo.Run("add", ".");
        _repo.Run("commit", "-m", "first");

        var common = new ToolCommon(
            new RepoRootResolver(TestGit.Executable),
            new GitProcessRunner(NullLogger<GitProcessRunner>.Instance),
            new SessionMetrics(),
            NullLoggerFactory.Instance,
            TestGit.Executable);
        _tool = new GitStatusTool(common);
    }

    public void Dispose() => _repo.Dispose();

    [Fact]
    public async Task Status_WithFilterDriverForStatDirtyFile_DoesNotRunFilter()
    {
        // Arrange
        var marker = _repo.ConfigureFilterDriverTrap(TrackedFile);

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var status = Assert.IsType<StatusResult>(envelope.Results[0]);
        Assert.True(status.IsClean);
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task Status_WithSubmoduleFilterDriver_DoesNotRunFilter()
    {
        // Arrange
        var marker = _repo.AddSubmoduleWithFilterDriverTrap(SubmodulePath);

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var status = Assert.IsType<StatusResult>(envelope.Results[0]);
        Assert.True(status.IsClean);
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task Status_OutputCutInsideTheSecondUntrackedPath_ReturnsOnlyTheWholeEntry()
    {
        // Arrange
        _repo.WriteFile(FirstUntrackedFile, "u\n");
        _repo.WriteFile(SecondUntrackedFile, "u\n");
        var tool = BuildTool(new OutputCuttingRunner(argv => argv.Contains(StatusSubcommand), KeptBytesInsideTheSecondUntracked));

        // Act
        var envelope = await tool.InvokeAsync(_repo.RepoPath, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.True(envelope.Truncated);
        var status = Assert.IsType<StatusResult>(Assert.Single(envelope.Results));
        Assert.Equal([FirstUntrackedFile], status.Untracked.Select(entry => entry.Path));
    }

    [Fact]
    public async Task Status_OutputCutInsideARenamesOriginalPath_LeavesTheRenameOut()
    {
        // Arrange
        _repo.Run("mv", TrackedFile, RenamedFile);
        var tool = BuildTool(new OutputCuttingRunner(argv => argv.Contains(StatusSubcommand), KeptBytesInsideTheOriginalPath));

        // Act
        var envelope = await tool.InvokeAsync(_repo.RepoPath, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.True(envelope.Truncated);
        var status = Assert.IsType<StatusResult>(Assert.Single(envelope.Results));
        Assert.Empty(status.Staged);
    }

    [Fact]
    public async Task Status_OutputCutRightAfterARenamesPath_LeavesTheRenameOut()
    {
        // Arrange
        _repo.Run("mv", TrackedFile, RenamedFile);
        var tool = BuildTool(new OutputCuttingRunner(argv => argv.Contains(StatusSubcommand), KeptBytesAfterTheRenamedPath));

        // Act
        var envelope = await tool.InvokeAsync(_repo.RepoPath, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.True(envelope.Truncated);
        var status = Assert.IsType<StatusResult>(Assert.Single(envelope.Results));
        Assert.Empty(status.Staged);
    }

    [Fact]
    public async Task Status_OutputCutRightAfterTheLastNul_ReturnsEveryEntry()
    {
        // Arrange
        _repo.Run("mv", TrackedFile, RenamedFile);
        var tool = BuildTool(new OutputCuttingRunner(argv => argv.Contains(StatusSubcommand), KeptEveryStatusByte));

        // Act
        var envelope = await tool.InvokeAsync(_repo.RepoPath, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.True(envelope.Truncated);
        var status = Assert.IsType<StatusResult>(Assert.Single(envelope.Results));
        var rename = Assert.Single(status.Staged);
        Assert.Equal(RenamedFile, rename.Path);
        Assert.Equal(TrackedFile, rename.OrigPath);
    }

    #region Test Helpers

    /// <summary>Build the tool under test over <paramref name="runner"/>, with the real root resolver.</summary>
    /// <param name="runner">The runner every git process of the call goes through.</param>
    /// <returns>The tool.</returns>
    private static GitStatusTool BuildTool(IGitProcessRunner runner)
    {
        var common = new ToolCommon(
            new RepoRootResolver(TestGit.Executable),
            runner,
            new SessionMetrics(),
            NullLoggerFactory.Instance,
            TestGit.Executable);
        return new GitStatusTool(common);
    }

    #endregion
}