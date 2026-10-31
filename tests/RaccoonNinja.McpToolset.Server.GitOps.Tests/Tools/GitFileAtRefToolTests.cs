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
    private const string BigFile = "big.txt";
    private const string OneLineFile = "oneline.txt";
    private const string BinaryFile = "blob.bin";
    private const int PageBytes = 4096;

    private readonly TempGitRepo _repo;
    private readonly GitFileAtRefTool _tool;

    public GitFileAtRefToolTests()
    {
        _repo = new TempGitRepo();
        _repo.WriteFile("dir/nested.txt", "nested\n");
        _repo.WriteFile("gone.txt", "bye\n");
        _repo.WriteBytes(BinaryFile, [0x89, 0x50, 0x00, 0x0A, 0x1A]);
        _repo.WriteFile(BigFile, string.Concat(Enumerable.Range(1, 1000).Select(n => $"line {n}\n")));
        _repo.WriteFile(OneLineFile, string.Concat(Enumerable.Repeat("0123456789", 5000)));
        _repo.Run("add", ".");
        _repo.Run("commit", "-m", "add files");
        _repo.Run("rm", "gone.txt");
        _repo.Run("commit", "-m", "remove gone.txt");

        var common = new ToolCommon(
            new RepoRootResolver(TestGit.Executable),
            new GitProcessRunner(NullLogger<GitProcessRunner>.Instance),
            new SessionMetrics(),
            NullLoggerFactory.Instance,
            TestGit.Executable);
        _tool = new GitFileAtRefTool(common, new RefVerifier(TestGit.Executable));
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
        var envelope = await _tool.InvokeAsync(cwd, BinaryFile, cancellationToken: TestContext.Current.CancellationToken);

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

    [Fact]
    public async Task LineRange_ReturnsOnlyThoseLines()
    {
        // Arrange
        var cwd = _repo.RepoPath;

        // Act
        var envelope = await _tool.InvokeAsync(cwd, BigFile, lineStart: 1, lineEnd: 200, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var file = Assert.IsType<FileAtRef>(envelope.Results[0]);
        Assert.StartsWith("line 1\n", file.Content);
        Assert.EndsWith("\nline 200\n", file.Content);
        Assert.Equal(1000, file.TotalLines);
        Assert.False(envelope.Truncated);
        Assert.True(envelope.FiltersApplied.ContainsKey("line_range"));
    }

    [Fact]
    public async Task MaxBytes_TruncatesAndReportsNextOffset()
    {
        // Arrange
        var cwd = _repo.RepoPath;

        // Act
        var envelope = await _tool.InvokeAsync(cwd, OneLineFile, maxBytes: PageBytes, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var file = Assert.IsType<FileAtRef>(envelope.Results[0]);
        Assert.Equal(PageBytes, file.Content.Length);
        Assert.True(envelope.Truncated);
        Assert.Equal(PageBytes, file.NextByteOffset);
        Assert.True(envelope.FiltersApplied.ContainsKey("max_bytes"));
    }

    [Fact]
    public async Task ByteOffset_ContinuesWhereTheCapStopped()
    {
        // Arrange
        var cwd = _repo.RepoPath;

        // Act
        var envelope = await _tool.InvokeAsync(cwd, OneLineFile, byteOffset: PageBytes, maxBytes: PageBytes, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var file = Assert.IsType<FileAtRef>(envelope.Results[0]);
        Assert.Equal(PageBytes, file.Content.Length);
        Assert.Equal('6', file.Content[0]);
        Assert.Equal(2 * PageBytes, file.NextByteOffset);
        Assert.True(envelope.FiltersApplied.ContainsKey("byte_offset"));
    }

    [Fact]
    public async Task ByteOffset_LastPage_IsNotTruncated()
    {
        // Arrange
        var cwd = _repo.RepoPath;

        // Act
        var envelope = await _tool.InvokeAsync(cwd, OneLineFile, byteOffset: 49000, maxBytes: PageBytes, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var file = Assert.IsType<FileAtRef>(envelope.Results[0]);
        Assert.Equal(1000, file.Content.Length);
        Assert.False(envelope.Truncated);
        Assert.Null(file.NextByteOffset);
    }

    [Fact]
    public async Task NoOptions_ReportsTotalLines()
    {
        // Arrange
        var cwd = _repo.RepoPath;

        // Act
        var envelope = await _tool.InvokeAsync(cwd, "dir/nested.txt", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var file = Assert.IsType<FileAtRef>(envelope.Results[0]);
        Assert.Equal(1, file.TotalLines);
        Assert.Null(file.NextByteOffset);
        Assert.False(envelope.Truncated);
    }

    [Fact]
    public async Task BinaryBlob_IgnoresRangeOffsetAndCap()
    {
        // Arrange
        var cwd = _repo.RepoPath;

        // Act
        var envelope = await _tool.InvokeAsync(cwd, BinaryFile, lineStart: 1, lineEnd: 1, byteOffset: 0, maxBytes: 1, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var file = Assert.IsType<FileAtRef>(envelope.Results[0]);
        Assert.True(file.IsBinary);
        Assert.Null(file.Content);
        Assert.Null(file.TotalLines);
        Assert.Null(file.NextByteOffset);
    }

    [Fact]
    public async Task BinaryBlob_MalformedRange_IsRejected()
    {
        // Arrange
        var cwd = _repo.RepoPath;

        // Act
        var envelope = await _tool.InvokeAsync(cwd, BinaryFile, lineStart: 2, lineEnd: 1, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.RejectedArgument, envelope.Error.Code);
        Assert.Equal("line_range", envelope.Error.Detail["param"]);
    }

    [Fact]
    public async Task MissingBlobWithPromisorRemote_FailsWithoutRunningUploadPack()
    {
        // Arrange
        var marker = _repo.ConfigureLazyFetchTrap(BigFile);

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, BigFile, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.GitCommandError, envelope.Error.Code);
        Assert.False(File.Exists(marker));
    }
}