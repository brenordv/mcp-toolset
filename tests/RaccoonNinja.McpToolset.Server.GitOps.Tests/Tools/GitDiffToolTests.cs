using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Server.GitOps.Errors;
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
    private const string BinaryFile = "x.bin";
    private const string GitlinkMode = "160000";
    private const string SubmodulePath = "sub";
    private const string SubmoduleOldCommit = "1111111111111111111111111111111111111111";
    private const string SubmoduleNewCommit = "2222222222222222222222222222222222222222";
    private const string SubmoduleMoveSubject = "move submodule";
    private const string SpaceBPath = "x b/c.txt";
    private const string DoubleQuotePath = "a\"b.txt";
    private const string AddedFileContent = "one\n";
    private const string AddedFileSubject = "add file";
    private const string ConflictFile = "m.txt";
    private const string LaterFile = "z.txt";
    private const string TheirsBranch = "theirs";
    private const string MainBranch = "main";
    private const string BaseSubject = "base";
    private const string BaseContent = "base\n";
    private const string OursContent = "ours\n";
    private const string TwoLineContent = "one\ntwo\n";
    private const string ThreeLineContent = "one\ntwo\nthree\n";
    private const string ForgedUnmergedPath = "m\ndiff --git a/fake.txt b/fake.txt\n@@ -0,0 +1 @@\n+fake";
    private const string UnreadablePatchMessage = "a changed path holds a line break while a path is unmerged; the patch cannot be read safely";
    private const string CutNumstatMessage = "the --numstat pass hit the output cap while a path is unmerged; the patch cannot be read safely";
    private const string DeleteSubject = "delete";
    private const string ModifySubject = "modify";

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

        _tool = BuildTool(new GitProcessRunner(NullLogger<GitProcessRunner>.Instance));
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

    [Fact]
    public async Task Diff_Worktree_WithUnmergedPath_KeepsCombinedHunksOffOtherFiles()
    {
        // Arrange
        _repo.WriteFile(ConflictFile, BaseContent);
        _repo.WriteFile(LaterFile, AddedFileContent);
        _repo.Run("add", ".");
        _repo.Run("commit", "-m", BaseSubject);
        _repo.Run("checkout", "-b", TheirsBranch);
        _repo.WriteFile(ConflictFile, "theirs\n");
        _repo.Run("commit", "-am", "their edit");
        _repo.Run("checkout", MainBranch);
        _repo.WriteFile(ConflictFile, OursContent);
        _repo.Run("commit", "-am", "our edit");
        _repo.MergeLeavingConflicts(TheirsBranch);
        _repo.WriteFile(ChangedFile, ThreeLineContent);
        _repo.WriteFile(LaterFile, TwoLineContent);

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var diff = Assert.IsType<DiffResult>(envelope.Results[0]);
        Assert.Equal([ChangedFile, LaterFile], diff.Files.Select(file => file.Path));
        Assert.All(diff.Files, file => Assert.Equal(ChangeType.Modified, file.ChangeType));
        Assert.All(diff.Files, file => Assert.Single(file.Hunks));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Diff_WithUnmergedPathHoldingLineBreak_FailsWithGitCommandError(bool staged)
    {
        // Arrange
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows does not allow a line break in a file name");
        }

        _repo.WriteFile(ForgedUnmergedPath, BaseContent);
        _repo.Run("add", ".");
        _repo.Run("commit", "-m", BaseSubject);
        _repo.Run("checkout", "-b", TheirsBranch);
        _repo.Run("rm", "--", ForgedUnmergedPath);
        _repo.Run("commit", "-m", DeleteSubject);
        _repo.Run("checkout", MainBranch);
        _repo.WriteFile(ForgedUnmergedPath, OursContent);
        _repo.Run("commit", "-am", ModifySubject);
        _repo.MergeLeavingConflicts(TheirsBranch);

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, staged: staged, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.GitCommandError, envelope.Error.Code);
        Assert.Equal(UnreadablePatchMessage, envelope.Error.Message);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("HEAD", false)]
    [InlineData(null, true)]
    public async Task Diff_AgainstWorktreeWithStatDirtyFile_LeavesIndexUnchanged(string fromRef, bool statOnly)
    {
        // Arrange
        _repo.MakeStatDirty(ChangedFile);
        var indexPath = Path.Combine(_repo.RepoPath, ".git", "index");
        var indexBefore = File.ReadAllBytes(indexPath);

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, fromRef: fromRef, statOnly: statOnly, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(indexBefore, File.ReadAllBytes(indexPath));
    }

    [Fact]
    public async Task Diff_StatOnlyWithStatDirtyFiles_ListsOnlyBinaryFileWithZeroCounts()
    {
        // Arrange
        _repo.WriteBytes(BinaryFile, [0, 1, 2, 0, 3]);
        _repo.Run("add", ".");
        _repo.Run("commit", "-m", AddedFileSubject);
        _repo.MakeStatDirty(ChangedFile);
        _repo.MakeStatDirty(BinaryFile);

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, statOnly: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var diff = Assert.IsType<DiffResult>(envelope.Results[0]);
        var file = Assert.Single(diff.Files);
        Assert.Equal(BinaryFile, file.Path);
        Assert.Equal(ChangeType.Modified, file.ChangeType);
        Assert.Equal(0, file.Additions);
        Assert.Equal(0, file.Deletions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Diff_WithUnmergedPathAndCutNumstat_FailsWithGitCommandError(bool staged)
    {
        // Arrange
        _repo.WriteFile(ConflictFile, BaseContent);
        _repo.Run("add", ".");
        _repo.Run("commit", "-m", BaseSubject);
        _repo.Run("checkout", "-b", TheirsBranch);
        _repo.Run("rm", "--", ConflictFile);
        _repo.Run("commit", "-m", DeleteSubject);
        _repo.Run("checkout", MainBranch);
        _repo.WriteFile(ConflictFile, OursContent);
        _repo.Run("commit", "-am", ModifySubject);
        _repo.MergeLeavingConflicts(TheirsBranch);
        var tool = BuildTool(new NumstatTruncatingRunner());

        // Act
        var envelope = await tool.InvokeAsync(_repo.RepoPath, staged: staged, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.GitCommandError, envelope.Error.Code);
        Assert.Equal(CutNumstatMessage, envelope.Error.Message);
    }

    #region Test Helpers

    /// <summary>Build the tool under test over <paramref name="runner"/>, with the real root resolver and ref verifier.</summary>
    /// <param name="runner">The runner every git process of the call goes through.</param>
    /// <returns>The tool.</returns>
    private static GitDiffTool BuildTool(IGitProcessRunner runner)
    {
        var common = new ToolCommon(
            new RepoRootResolver(TestGit.Executable),
            runner,
            new SessionMetrics(),
            NullLoggerFactory.Instance,
            TestGit.Executable);
        return new GitDiffTool(common, new RefVerifier(TestGit.Executable));
    }

    /// <summary>Runs git for real and marks the result of every <c>--numstat</c> pass as cut at the output cap.</summary>
    private sealed class NumstatTruncatingRunner : IGitProcessRunner
    {
        private readonly GitProcessRunner _inner = new(NullLogger<GitProcessRunner>.Instance);

        public async Task<RunResult> RunAsync(
            IList<string> argv,
            IDictionary<string, string> env,
            string workingDirectory,
            int? timeoutMs = null,
            int? outputCapBytes = null,
            int? callId = null,
            CancellationToken cancellationToken = default)
        {
            var result = await _inner.RunAsync(argv, env, workingDirectory, timeoutMs, outputCapBytes, callId, cancellationToken);
            return argv.Contains("--numstat") ? result with { Truncated = true } : result;
        }
    }

    #endregion
}