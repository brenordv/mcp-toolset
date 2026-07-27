using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Server.GitOps.Metrics;
using RaccoonNinja.McpToolset.Server.GitOps.Models;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;
using RaccoonNinja.McpToolset.Server.GitOps.Tools;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Tools;

/// <summary>
/// Proves three-dot (merge-base) diff semantics differ from two-dot on a divergent history:
/// <c>main</c> advances past the fork point while <c>feature</c> adds a file. This needs its own
/// branched repo, so it does not share the linear <see cref="Fixtures.GitRepoFixture"/>.
/// </summary>
public sealed class GitDiffRangeSemanticsTests : IAsyncLifetime
{
    private string _repoPath;
    private readonly ToolCommon _common;
    private readonly IRefVerifier _refVerifier;

    public GitDiffRangeSemanticsTests()
    {
        var resolver = new RepoRootResolver();
        var runner = new GitProcessRunner(NullLogger<GitProcessRunner>.Instance);
        var metrics = new SessionMetrics();
        _common = new ToolCommon(resolver, runner, metrics, NullLoggerFactory.Instance);
        _refVerifier = new RefVerifier();
    }

    public ValueTask InitializeAsync()
    {
        _repoPath = Path.Combine(Path.GetTempPath(), "mcp-gitops-range-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_repoPath);

        RunGit("init", "-b", "main");
        RunGit("config", "user.email", "tester@example.com");
        RunGit("config", "user.name", "Test User");
        RunGit("config", "commit.gpgsign", "false");

        File.WriteAllText(Path.Combine(_repoPath, "base.txt"), "base\n");
        RunGit("add", "base.txt");
        RunGit("commit", "-m", "base commit");

        RunGit("checkout", "-b", "feature");
        File.WriteAllText(Path.Combine(_repoPath, "feature.txt"), "feature\n");
        RunGit("add", "feature.txt");
        RunGit("commit", "-m", "add feature file");

        RunGit("checkout", "main");
        File.WriteAllText(Path.Combine(_repoPath, "base.txt"), "base changed on main\n");
        RunGit("add", "base.txt");
        RunGit("commit", "-m", "advance main past fork");

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        TryDelete(_repoPath);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ThreeDot_Range_Diffs_From_MergeBase_While_TwoDot_Diffs_Endpoints()
    {
        var tool = new GitDiffTool(_common, _refVerifier);

        var threeDot = await tool.InvokeAsync(_repoPath, fromRef: "main...feature");
        var twoRef = await tool.InvokeAsync(_repoPath, fromRef: "main", toRef: "feature");

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

    private static List<string> PathsOf(RaccoonNinja.McpToolset.Server.GitOps.Envelope.ResultEnvelope envelope)
        => ((DiffResult)envelope.Results[0]).Files
            .Select(f => f.Path)
            .OrderBy(p => p)
            .ToList();

    private void RunGit(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _repoPath,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi);
        if (process == null) throw new InvalidOperationException("failed to start git");
        process.StandardInput.Close();
        process.WaitForExit(15_000);
        if (process.ExitCode != 0)
        {
            var err = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed ({process.ExitCode}): {err}");
        }
    }

    private static void TryDelete(string path)
    {
        if (!Directory.Exists(path)) return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); }
                catch { /* ignore */ }
            }
            Directory.Delete(path, recursive: true);
        }
        catch { /* best effort */ }
    }
}