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
    private const string SubmoduleMoveSubject = "move submodule";
    private const string SpaceBPath = "x b/c.txt";
    private const string DoubleQuotePath = "a\"b.txt";
    private const string AddedFileContent = "one\n";
    private const string AddedFileSubject = "add file";

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
            new RepoRootResolver(TestGit.Executable),
            new GitProcessRunner(NullLogger<GitProcessRunner>.Instance),
            new SessionMetrics(),
            NullLoggerFactory.Instance,
            TestGit.Executable);
        _tool = new GitDiffTool(common, new RefVerifier(TestGit.Executable));
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

    [Theory]
    [InlineData("diff.submodule", "log")]
    [InlineData("diff.ignoreSubmodules", "all")]
    public async Task Diff_RefToRef_WithSubmoduleConfig_ReturnsSubmoduleEntry(string configKey, string configValue)
    {
        // Arrange
        _repo.Run("update-index", "--add", "--cacheinfo", $"{GitlinkMode},{SubmoduleOldCommit},{SubmodulePath}");
        _repo.Run("commit", "-m", "add submodule");
        _repo.Run("update-index", "--cacheinfo", $"{GitlinkMode},{SubmoduleNewCommit},{SubmodulePath}");
        _repo.Run("commit", "-m", SubmoduleMoveSubject);
        _repo.Run("config", configKey, configValue);

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

    [Theory]
    [InlineData("HEAD")]
    [InlineData(null)]
    public async Task Diff_FromParent_WithGitmodulesIgnoreAll_ReturnsSubmoduleEntry(string toRef)
    {
        // Arrange
        _repo.StageIgnoredSubmoduleMove(SubmodulePath);
        _repo.Run("commit", "-m", SubmoduleMoveSubject);

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, fromRef: "HEAD~1", toRef: toRef, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var diff = Assert.IsType<DiffResult>(envelope.Results[0]);
        var file = Assert.Single(diff.Files);
        Assert.Equal(SubmodulePath, file.Path);
        Assert.Equal(1, file.Additions);
        Assert.Equal(1, file.Deletions);
    }

    [Fact]
    public async Task Diff_Staged_WithGitmodulesIgnoreAll_ReturnsSubmoduleEntry()
    {
        // Arrange
        _repo.StageIgnoredSubmoduleMove(SubmodulePath);

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, staged: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var diff = Assert.IsType<DiffResult>(envelope.Results[0]);
        var file = Assert.Single(diff.Files);
        Assert.Equal(SubmodulePath, file.Path);
        Assert.Equal(1, file.Additions);
        Assert.Equal(1, file.Deletions);
    }

    [Fact]
    public async Task Diff_RefToRef_AddedPathHoldingSpaceB_ReturnsWholePath()
    {
        // Arrange
        _repo.WriteFile(SpaceBPath, AddedFileContent);
        _repo.Run("add", ".");
        _repo.Run("commit", "-m", AddedFileSubject);

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, fromRef: "HEAD~1", toRef: "HEAD", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var diff = Assert.IsType<DiffResult>(envelope.Results[0]);
        var file = Assert.Single(diff.Files);
        Assert.Equal(SpaceBPath, file.Path);
        Assert.Equal(ChangeType.Added, file.ChangeType);
        Assert.Equal(1, file.Additions);
    }

    [Fact]
    public async Task Diff_RefToRef_AddedPathHoldingDoubleQuote_ReturnsUnquotedPath()
    {
        // Arrange
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows does not allow a double quote in a file name");
        }

        _repo.WriteFile(DoubleQuotePath, AddedFileContent);
        _repo.Run("add", ".");
        _repo.Run("commit", "-m", AddedFileSubject);

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, fromRef: "HEAD~1", toRef: "HEAD", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var diff = Assert.IsType<DiffResult>(envelope.Results[0]);
        var file = Assert.Single(diff.Files);
        Assert.Equal(DoubleQuotePath, file.Path);
        Assert.Equal(ChangeType.Added, file.ChangeType);
        Assert.Equal(1, file.Additions);
    }
}