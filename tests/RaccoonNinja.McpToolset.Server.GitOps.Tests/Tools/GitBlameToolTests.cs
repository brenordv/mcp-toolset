using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Server.GitOps.Metrics;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;
using RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;
using RaccoonNinja.McpToolset.Server.GitOps.Tools;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Tools;

public sealed class GitBlameToolTests : IDisposable
{
    private const string TrackedFile = "a.txt";

    private readonly TempGitRepo _repo;
    private readonly GitBlameTool _tool;

    public GitBlameToolTests()
    {
        _repo = new TempGitRepo();
        _repo.WriteFile(TrackedFile, "one\n");
        _repo.Run("add", ".");
        _repo.Run("commit", "-m", "first");

        var common = new ToolCommon(
            new RepoRootResolver(),
            new GitProcessRunner(NullLogger<GitProcessRunner>.Instance),
            new SessionMetrics(),
            NullLoggerFactory.Instance);
        _tool = new GitBlameTool(common, new RefVerifier());
    }

    public void Dispose() => _repo.Dispose();

    [Fact]
    public async Task Blame_WorktreeFileWithFilterDriver_DoesNotRunFilter()
    {
        // Arrange
        var marker = _repo.ConfigureFilterDriverTrap(TrackedFile);

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, TrackedFile, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(1, envelope.Count);
        Assert.False(File.Exists(marker));
    }
}