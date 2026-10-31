using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Server.GitOps.Errors;
using RaccoonNinja.McpToolset.Server.GitOps.Metrics;
using RaccoonNinja.McpToolset.Server.GitOps.Models;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;
using RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;
using RaccoonNinja.McpToolset.Server.GitOps.Tools;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Tools;

public sealed class GitShowToolTests : IDisposable
{
    private const string ChangedFile = "a.txt";
    private const string SignedSubject = "signed";
    private const string SubmodulePath = "sub";
    private const string TagName = "v1";
    private const string TreeTagName = "tree-tag";
    private const string HeadTree = "HEAD^{tree}";
    private const string RegularFileMode = "100644";
    private const string ExecutableFileMode = "100755";

    private const string PatchShapedTagMessage =
        "diff --git a/fake.txt b/fake.txt\n" +
        "new file mode 100644\n" +
        "--- /dev/null\n" +
        "+++ b/fake.txt\n" +
        "@@ -0,0 +1 @@\n" +
        "+fake\n";

    private readonly TempGitRepo _repo;
    private readonly GitShowTool _tool;

    public GitShowToolTests()
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
        _tool = new GitShowTool(common, new RefVerifier(TestGit.Executable));
    }

    public void Dispose() => _repo.Dispose();

    [Theory]
    [InlineData("color.ui", "always")]
    [InlineData("diff.noprefix", "true")]
    public async Task Show_WithPatchShapeConfig_ReturnsFiles(string configKey, string configValue)
    {
        // Arrange
        _repo.Run("config", configKey, configValue);

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, @ref: "HEAD", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var payload = Assert.IsAssignableFrom<IDictionary<string, object>>(envelope.Results[0]);
        var files = Assert.IsAssignableFrom<IReadOnlyList<FileDiff>>(payload["files"]);
        var file = Assert.Single(files);
        Assert.Equal(ChangedFile, file.Path);
        Assert.NotEmpty(file.Hunks);
    }

    [Fact]
    public async Task Show_WithShowSignatureConfigAndSignedCommit_DoesNotRunSignatureProgram()
    {
        // Arrange
        var signedHash = _repo.CommitWithForgedSignature(SignedSubject);
        var marker = _repo.ConfigureSignatureProgramTrap();

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, @ref: "HEAD", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var payload = Assert.IsAssignableFrom<IDictionary<string, object>>(envelope.Results[0]);
        var commit = Assert.IsType<Commit>(payload["commit"]);
        Assert.Equal(signedHash, commit.Hash);
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task Show_WithGitmodulesIgnoreAll_ReturnsSubmoduleEntry()
    {
        // Arrange
        _repo.StageIgnoredSubmoduleMove(SubmodulePath);
        _repo.Run("commit", "-m", "move submodule");

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, @ref: "HEAD", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var payload = Assert.IsAssignableFrom<IDictionary<string, object>>(envelope.Results[0]);
        var files = Assert.IsAssignableFrom<IReadOnlyList<FileDiff>>(payload["files"]);
        var file = Assert.Single(files);
        Assert.Equal(SubmodulePath, file.Path);
        Assert.Equal(1, file.Additions);
        Assert.Equal(1, file.Deletions);
    }

    [Fact]
    public async Task Show_AnnotatedTagWithPatchShapedMessage_ReturnsOnlyTheCommitFiles()
    {
        // Arrange
        _repo.Run("tag", "--no-sign", "-a", TagName, "-m", PatchShapedTagMessage);
        var headHash = _repo.Run("rev-parse", "HEAD");

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, @ref: TagName, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var payload = Assert.IsAssignableFrom<IDictionary<string, object>>(envelope.Results[0]);
        var commit = Assert.IsType<Commit>(payload["commit"]);
        Assert.Equal(headHash, commit.Hash);
        var files = Assert.IsAssignableFrom<IReadOnlyList<FileDiff>>(payload["files"]);
        var file = Assert.Single(files);
        Assert.Equal(ChangedFile, file.Path);
        Assert.Single(file.Hunks);
    }

    [Theory]
    [InlineData(HeadTree)]
    [InlineData($"HEAD:{ChangedFile}")]
    public async Task Show_RefNamingTreeOrBlob_IsRejected(string revision)
    {
        // Arrange
        var objectId = _repo.Run("rev-parse", revision);

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, @ref: objectId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.RejectedArgument, envelope.Error.Code);
        Assert.Equal("ref", envelope.Error.Detail["param"]);
    }

    [Fact]
    public async Task Show_AnnotatedTagOfTreeWithPatchShapedMessage_IsRejected()
    {
        // Arrange
        var treeId = _repo.Run("rev-parse", HeadTree);
        _repo.Run("tag", "--no-sign", "-a", TreeTagName, "-m", PatchShapedTagMessage, treeId);

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, @ref: TreeTagName, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.RejectedArgument, envelope.Error.Code);
        Assert.Equal("ref", envelope.Error.Detail["param"]);
    }

    [Fact]
    public async Task Show_ModeOnlyChange_ReturnsOldAndNewMode()
    {
        // Arrange
        _repo.Run("update-index", "--chmod=+x", ChangedFile);
        _repo.Run("commit", "-m", "make a.txt executable");

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, @ref: "HEAD", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var payload = Assert.IsAssignableFrom<IDictionary<string, object>>(envelope.Results[0]);
        var files = Assert.IsAssignableFrom<IReadOnlyList<FileDiff>>(payload["files"]);
        var file = Assert.Single(files);
        Assert.Equal(ChangedFile, file.Path);
        Assert.Equal(RegularFileMode, file.OldMode);
        Assert.Equal(ExecutableFileMode, file.NewMode);
        Assert.Empty(file.Hunks);
    }

    [Fact]
    public async Task Show_RootCommitWithShowRootOff_ReturnsAddedFile()
    {
        // Arrange
        var rootCommit = _repo.ConfigureShowRootOff();

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, @ref: rootCommit, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var payload = Assert.IsAssignableFrom<IDictionary<string, object>>(envelope.Results[0]);
        var files = Assert.IsAssignableFrom<IReadOnlyList<FileDiff>>(payload["files"]);
        var file = Assert.Single(files);
        Assert.Equal(ChangedFile, file.Path);
        Assert.Equal(ChangeType.Added, file.ChangeType);
        Assert.Equal(1, file.Additions);
    }
}