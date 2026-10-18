using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Server.GitOps.Errors;
using RaccoonNinja.McpToolset.Server.GitOps.Metrics;
using RaccoonNinja.McpToolset.Server.GitOps.Models;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;
using RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;
using RaccoonNinja.McpToolset.Server.GitOps.Tools;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Tools;

public sealed class GitFileAtRefToolTests : IDisposable
{
    private readonly TempGitRepo _repo;
    private readonly GitFileAtRefTool _tool;

    public GitFileAtRefToolTests()
    {
        _repo = new TempGitRepo();
        _repo.WriteFile("dir/nested.txt", "nested\n");
        _repo.WriteFile("gone.txt", "bye\n");
        _repo.WriteBytes("blob.bin", [0x89, 0x50, 0x00, 0x0A, 0x1A]);
        _repo.Run("add", ".");
        _repo.Run("commit", "-m", "add files");
        _repo.Run("rm", "gone.txt");
        _repo.Run("commit", "-m", "remove gone.txt");

        var common = new ToolCommon(
            new RepoRootResolver(),
            new GitProcessRunner(NullLogger<GitProcessRunner>.Instance),
            new SessionMetrics(),
            NullLoggerFactory.Instance);
        _tool = new GitFileAtRefTool(common, new RefVerifier());
    }

    public void Dispose() => _repo.Dispose();

    [Fact]
    public async Task NestedPath_ReturnsItsContentAtHead()
    {
        // Arrange
        var cwd = _repo.RepoPath;

        // Act
        var envelope = await _tool.InvokeAsync(cwd, "dir/nested.txt", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var file = Assert.IsType<FileAtRef>(envelope.Results[0]);
        Assert.Equal("dir/nested.txt", file.Path);
        Assert.Equal("nested\n", file.Content);
    }

    [Fact]
    public async Task DeletedFile_IsReadableAtEarlierRef_AndMissingAtHead()
    {
        // Arrange
        var cwd = _repo.RepoPath;

        // Act
        var earlier = await _tool.InvokeAsync(cwd, "gone.txt", @ref: "HEAD~1", cancellationToken: TestContext.Current.CancellationToken);
        var head = await _tool.InvokeAsync(cwd, "gone.txt", @ref: "HEAD", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(earlier.Error);
        Assert.Equal("bye\n", Assert.IsType<FileAtRef>(earlier.Results[0]).Content);
        Assert.NotNull(head.Error);
        Assert.Equal(ErrorCodes.PathNotFound, head.Error.Code);
    }

    [Fact]
    public async Task BinaryBlob_IsFlaggedWithNullContent()
    {
        // Arrange
        var cwd = _repo.RepoPath;

        // Act
        var envelope = await _tool.InvokeAsync(cwd, "blob.bin", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var file = Assert.IsType<FileAtRef>(envelope.Results[0]);
        Assert.True(file.IsBinary);
        Assert.Null(file.Content);
        Assert.Equal(5, file.Size);
    }

    [Theory]
    [InlineData("dir")]
    [InlineData("dir/")]
    public async Task DirectoryPath_IsRejected(string path)
    {
        // Arrange
        var cwd = _repo.RepoPath;

        // Act
        var envelope = await _tool.InvokeAsync(cwd, path, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.RejectedArgument, envelope.Error.Code);
        Assert.Equal("path", envelope.Error.Detail["param"]);
    }
}