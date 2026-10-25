using Microsoft.Extensions.Logging.Abstractions;
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
            new RepoRootResolver(),
            new GitProcessRunner(NullLogger<GitProcessRunner>.Instance),
            new SessionMetrics(),
            NullLoggerFactory.Instance);
        _tool = new GitShowTool(common, new RefVerifier());
    }

    public void Dispose() => _repo.Dispose();

    [Fact]
    public async Task Show_WithColorUiAlways_ReturnsFiles()
    {
        // Arrange
        _repo.Run("config", "color.ui", "always");

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
}