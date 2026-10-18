using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Server.GitOps.Metrics;
using RaccoonNinja.McpToolset.Server.GitOps.Models;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;
using RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;
using RaccoonNinja.McpToolset.Server.GitOps.Tools;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Tools;

public sealed class GitLsFilesStageTests : IDisposable
{
    private readonly TempGitRepo _repo;
    private readonly GitLsFilesTool _tool;

    public GitLsFilesStageTests()
    {
        _repo = new TempGitRepo();
        _repo.WriteFile("plain.txt", "plain\n");
        _repo.WriteFile("run.sh", "#!/bin/sh\necho hi\n");
        _repo.Run("add", "plain.txt", "run.sh");
        _repo.Run("commit", "-m", "add plain and script");
        _repo.Run("update-index", "--chmod=+x", "run.sh");
        _repo.Run("commit", "-m", "make run.sh executable");

        var common = new ToolCommon(
            new RepoRootResolver(),
            new GitProcessRunner(NullLogger<GitProcessRunner>.Instance),
            new SessionMetrics(),
            NullLoggerFactory.Instance);
        _tool = new GitLsFilesTool(common);
    }

    public void Dispose() => _repo.Dispose();

    [Fact]
    public async Task GitLsFilesTool_Stage_ReportsModes()
    {
        // Arrange
        var cwd = _repo.RepoPath;

        // Act
        var envelope = await _tool.InvokeAsync(cwd, stage: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var entries = envelope.Results.Cast<LsFilesEntry>().ToList();
        var script = Assert.Single(entries, entry => entry.Path == "run.sh");
        Assert.Equal("100755", script.Mode);
        Assert.Equal(0, script.Stage);
        var plain = Assert.Single(entries, entry => entry.Path == "plain.txt");
        Assert.Equal("100644", plain.Mode);
        Assert.All(entries, entry => Assert.True(RefVerifier.IsResolvedObjectName(entry.ObjectId)));
    }

    [Fact]
    public async Task GitLsFilesTool_Default_StillReturnsPathStrings()
    {
        // Arrange
        var cwd = _repo.RepoPath;

        // Act
        var envelope = await _tool.InvokeAsync(cwd, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.All(envelope.Results, item => Assert.IsType<string>(item));
        Assert.Contains("plain.txt", envelope.Results);
        Assert.Contains("run.sh", envelope.Results);
    }
}