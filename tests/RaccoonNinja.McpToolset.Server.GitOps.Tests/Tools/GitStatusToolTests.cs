using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Server.GitOps.Metrics;
using RaccoonNinja.McpToolset.Server.GitOps.Models;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;
using RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;
using RaccoonNinja.McpToolset.Server.GitOps.Tools;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Tools;

public sealed class GitStatusToolTests : IDisposable
{
    private const string TrackedFile = "a.txt";
    private const string SubmodulePath = "sub";

    private readonly TempGitRepo _repo;
    private readonly GitStatusTool _tool;

    public GitStatusToolTests()
    {
        _repo = new TempGitRepo();
        _repo.WriteFile(TrackedFile, "one\n");
        _repo.Run("add", ".");
        _repo.Run("commit", "-m", "first");

        var common = new ToolCommon(
            new RepoRootResolver(TestGit.Executable),
            new GitProcessRunner(NullLogger<GitProcessRunner>.Instance),
            new SessionMetrics(),
            NullLoggerFactory.Instance,
            TestGit.Executable);
        _tool = new GitStatusTool(common);
    }

    public void Dispose() => _repo.Dispose();

    [Fact]
    public async Task Status_WithFilterDriverForStatDirtyFile_DoesNotRunFilter()
    {
        // Arrange
        var marker = _repo.ConfigureFilterDriverTrap(TrackedFile);

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var status = Assert.IsType<StatusResult>(envelope.Results[0]);
        Assert.True(status.IsClean);
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task Status_WithSubmoduleFilterDriver_DoesNotRunFilter()
    {
        // Arrange
        var marker = _repo.AddSubmoduleWithFilterDriverTrap(SubmodulePath);

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var status = Assert.IsType<StatusResult>(envelope.Results[0]);
        Assert.True(status.IsClean);
        Assert.False(File.Exists(marker));
    }
}