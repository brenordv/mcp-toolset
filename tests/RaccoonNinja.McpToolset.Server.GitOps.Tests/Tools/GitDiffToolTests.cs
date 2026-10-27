using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Server.GitOps.Metrics;
using RaccoonNinja.McpToolset.Server.GitOps.Models;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;
using RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;
using RaccoonNinja.McpToolset.Server.GitOps.Tools;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Tools;

public sealed class GitDiffToolTests : IDisposable
{
    private const string ChangedFile = "a.txt";
    private const string GitlinkMode = "160000";
    private const string SubmodulePath = "sub";
    private const string SubmoduleOldCommit = "1111111111111111111111111111111111111111";
    private const string SubmoduleNewCommit = "2222222222222222222222222222222222222222";

    private readonly TempGitRepo _repo;
    private readonly GitDiffTool _tool;

    public GitDiffToolTests()
    {
        _repo = new TempGitRepo();
        _repo.WriteFile(ChangedFile, "one\n");
        _repo.Run("add", ".");
        _repo.Run("commit", "-m", "first");
        _repo.WriteFile(ChangedFile, "one\ntwo\n");
        _repo.Run("add", ".");
        _repo.Run("commit", "-m", "second");

        var common = new ToolCommon(
            new RepoRootResolver(),
            new GitProcessRunner(NullLogger<GitProcessRunner>.Instance),
            new SessionMetrics(),
            NullLoggerFactory.Instance);
        _tool = new GitDiffTool(common, new RefVerifier());
    }

    public void Dispose() => _repo.Dispose();

    [Theory]
    [InlineData("color.ui", "always")]
    [InlineData("color.diff", "always")]
    [InlineData("diff.noprefix", "true")]
    public async Task Diff_RefToRef_WithPatchShapeConfig_ReturnsChangedFile(string configKey, string configValue)
    {
        // Arrange
        _repo.Run("config", configKey, configValue);

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, fromRef: "HEAD~1", toRef: "HEAD", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var diff = Assert.IsType<DiffResult>(envelope.Results[0]);
        var file = Assert.Single(diff.Files);
        Assert.Equal(ChangedFile, file.Path);
        Assert.Equal(1, file.Additions);
        Assert.NotEmpty(file.Hunks);
    }

    [Fact]
    public async Task Diff_Worktree_WithMnemonicPrefixConfig_ReturnsChangedFile()
    {
        // Arrange
        _repo.WriteFile(ChangedFile, "one\ntwo\nthree\n");
        _repo.Run("config", "diff.mnemonicPrefix", "true");

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var diff = Assert.IsType<DiffResult>(envelope.Results[0]);
        var file = Assert.Single(diff.Files);
        Assert.Equal(ChangedFile, file.Path);
        Assert.Equal(1, file.Additions);
        Assert.NotEmpty(file.Hunks);
    }

    [Fact]
    public async Task Diff_RefToRef_WithSubmoduleLogConfig_ReturnsSubmoduleEntry()
    {
        // Arrange
        _repo.Run("update-index", "--add", "--cacheinfo", $"{GitlinkMode},{SubmoduleOldCommit},{SubmodulePath}");
        _repo.Run("commit", "-m", "add submodule");
        _repo.Run("update-index", "--cacheinfo", $"{GitlinkMode},{SubmoduleNewCommit},{SubmodulePath}");
        _repo.Run("commit", "-m", "move submodule");
        _repo.Run("config", "diff.submodule", "log");

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, fromRef: "HEAD~1", toRef: "HEAD", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var diff = Assert.IsType<DiffResult>(envelope.Results[0]);
        var file = Assert.Single(diff.Files);
        Assert.Equal(SubmodulePath, file.Path);
        Assert.Equal(1, file.Additions);
        Assert.Equal(1, file.Deletions);
    }

    [Fact]
    public async Task Diff_WorktreeWithFilterDriverForStatDirtyFile_DoesNotRunFilter()
    {
        // Arrange
        var marker = _repo.ConfigureFilterDriverTrap(ChangedFile);

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var diff = Assert.IsType<DiffResult>(envelope.Results[0]);
        Assert.Empty(diff.Files);
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task Diff_WorktreeWithSubmoduleFilterDriver_DoesNotRunFilter()
    {
        // Arrange
        var marker = _repo.AddSubmoduleWithFilterDriverTrap(SubmodulePath);

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var diff = Assert.IsType<DiffResult>(envelope.Results[0]);
        Assert.Empty(diff.Files);
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task Diff_FromRefWithSubmoduleFilterDriver_DoesNotRunFilter()
    {
        // Arrange
        var marker = _repo.AddSubmoduleWithFilterDriverTrap(SubmodulePath);

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, fromRef: "HEAD", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var diff = Assert.IsType<DiffResult>(envelope.Results[0]);
        Assert.Empty(diff.Files);
        Assert.False(File.Exists(marker));
    }
}