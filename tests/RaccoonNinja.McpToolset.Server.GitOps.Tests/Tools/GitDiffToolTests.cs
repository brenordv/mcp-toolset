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
    [InlineData("color.ui")]
    [InlineData("color.diff")]
    public async Task Diff_RefToRef_WithColorConfigAlways_ReturnsChangedFile(string colorKey)
    {
        // Arrange
        _repo.Run("config", colorKey, "always");

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
}