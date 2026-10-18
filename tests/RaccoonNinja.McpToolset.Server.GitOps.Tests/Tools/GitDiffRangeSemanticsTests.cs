using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Server.GitOps.Envelope;
using RaccoonNinja.McpToolset.Server.GitOps.Metrics;
using RaccoonNinja.McpToolset.Server.GitOps.Models;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;
using RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;
using RaccoonNinja.McpToolset.Server.GitOps.Tools;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Tools;

/// <summary>
/// Proves three-dot (merge-base) diff semantics differ from two-dot on a divergent history:
/// <c>main</c> advances past the fork point while <c>feature</c> adds a file. This needs its own
/// branched repo (a <see cref="TempGitRepo"/>), so it does not share the linear <see cref="GitRepoFixture"/>.
/// </summary>
public sealed class GitDiffRangeSemanticsTests : IDisposable
{
    private readonly TempGitRepo _repo;
    private readonly ToolCommon _common;
    private readonly IRefVerifier _refVerifier;

    public GitDiffRangeSemanticsTests()
    {
        _repo = new TempGitRepo();
        _repo.WriteFile("base.txt", "base\n");
        _repo.Run("add", "base.txt");
        _repo.Run("commit", "-m", "base commit");

        _repo.Run("checkout", "-b", "feature");
        _repo.WriteFile("feature.txt", "feature\n");
        _repo.Run("add", "feature.txt");
        _repo.Run("commit", "-m", "add feature file");

        _repo.Run("checkout", "main");
        _repo.WriteFile("base.txt", "base changed on main\n");
        _repo.Run("add", "base.txt");
        _repo.Run("commit", "-m", "advance main past fork");

        var resolver = new RepoRootResolver();
        var runner = new GitProcessRunner(NullLogger<GitProcessRunner>.Instance);
        var metrics = new SessionMetrics();
        _common = new ToolCommon(resolver, runner, metrics, NullLoggerFactory.Instance);
        _refVerifier = new RefVerifier();
    }

    public void Dispose() => _repo.Dispose();

    [Fact]
    public async Task ThreeDot_Range_Diffs_From_MergeBase_While_TwoDot_Diffs_Endpoints()
    {
        // Arrange
        var tool = new GitDiffTool(_common, _refVerifier);

        // Act
        var threeDot = await tool.InvokeAsync(_repo.RepoPath, fromRef: "main...feature");
        var twoRef = await tool.InvokeAsync(_repo.RepoPath, fromRef: "main", toRef: "feature");

        // Assert
        Assert.Null(threeDot.Error);
        Assert.Null(twoRef.Error);

        var threeDotPaths = PathsOf(threeDot);
        var twoRefPaths = PathsOf(twoRef);

        Assert.Contains("feature.txt", threeDotPaths);
        Assert.DoesNotContain("base.txt", threeDotPaths);
        Assert.Contains("feature.txt", twoRefPaths);
        Assert.Contains("base.txt", twoRefPaths);
        Assert.NotEqual(twoRefPaths, threeDotPaths);
    }

    private static List<string> PathsOf(ResultEnvelope envelope)
        => ((DiffResult)envelope.Results[0]).Files
            .Select(f => f.Path)
            .OrderBy(p => p)
            .ToList();
}