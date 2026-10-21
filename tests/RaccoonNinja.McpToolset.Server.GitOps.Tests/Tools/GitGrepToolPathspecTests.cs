using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Server.GitOps.Envelope;
using RaccoonNinja.McpToolset.Server.GitOps.Errors;
using RaccoonNinja.McpToolset.Server.GitOps.Metrics;
using RaccoonNinja.McpToolset.Server.GitOps.Models;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;
using RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;
using RaccoonNinja.McpToolset.Server.GitOps.Tools;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Tools;

public sealed class GitGrepToolPathspecTests : IDisposable
{
    private const string Pattern = "name";

    private static readonly string[] AllFiles =
    [
        "crates/README.md",
        "crates/a/b/Cargo.toml",
        "crates/c/d/Cargo.toml",
        "dir/(a,b).txt",
        "env/app.cfg",
        "nested/x.json",
        "root.json",
        "src/main.cs",
        "src/sub/deep.cs",
    ];

    private readonly TempGitRepo _repo;
    private readonly GitGrepTool _tool;

    public GitGrepToolPathspecTests()
    {
        _repo = new TempGitRepo();
        foreach (var path in AllFiles)
        {
            _repo.WriteFile(path, $"name = {path}\n");
        }

        _repo.Run("add", ".");
        _repo.Run("commit", "-m", "add pathspec tree");

        var common = new ToolCommon(
            new RepoRootResolver(),
            new GitProcessRunner(NullLogger<GitProcessRunner>.Instance),
            new SessionMetrics(),
            NullLoggerFactory.Instance);
        _tool = new GitGrepTool(common, new RefVerifier());
    }

    public void Dispose() => _repo.Dispose();

    [Fact]
    public async Task Grep_WithGlobs_MatchesOnlyFilesOfThatShape()
    {
        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, Pattern, globs: ["crates/*/*/Cargo.toml"], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(["crates/a/b/Cargo.toml", "crates/c/d/Cargo.toml"], MatchedPaths(envelope));
    }

    [Fact]
    public async Task Grep_WithBasenameGlob_MatchesAtAnyDepth()
    {
        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, Pattern, globs: ["*.cs"], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(["src/main.cs", "src/sub/deep.cs"], MatchedPaths(envelope));
    }

    [Fact]
    public async Task Grep_WithExcludePath_DropsThatSubtree()
    {
        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, Pattern, excludePaths: ["env"], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var paths = MatchedPaths(envelope);
        Assert.Equal(AllFiles.Length - 1, paths.Count);
        Assert.DoesNotContain("env/app.cfg", paths);
        Assert.All(paths, path => Assert.Contains(path, AllFiles));
    }

    [Fact]
    public async Task Grep_WithExcludeWildcard_DropsMatchingFiles()
    {
        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, Pattern, excludePaths: ["*.json"], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var paths = MatchedPaths(envelope);
        Assert.Equal(AllFiles.Length - 2, paths.Count);
        Assert.DoesNotContain(paths, path => path.EndsWith(".json", StringComparison.Ordinal));
        Assert.All(paths, path => Assert.Contains(path, AllFiles));
    }

    [Fact]
    public async Task Grep_WithPathsAndExclude_AppliesBoth()
    {
        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, Pattern, paths: ["src"], excludePaths: ["src/sub"], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(["src/main.cs"], MatchedPaths(envelope));
    }

    [Fact]
    public async Task Grep_WithGlobsAtRef_MatchesAtThatRef()
    {
        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, Pattern, @ref: "HEAD", globs: ["*.json"], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(["nested/x.json", "root.json"], MatchedPaths(envelope));
    }

    [Fact]
    public async Task Grep_WithExcludePathAtRef_DropsThatSubtree()
    {
        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, Pattern, @ref: "HEAD", excludePaths: ["env"], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var paths = MatchedPaths(envelope);
        Assert.Equal(AllFiles.Length - 1, paths.Count);
        Assert.DoesNotContain("env/app.cfg", paths);
        Assert.All(paths, path => Assert.Contains(path, AllFiles));
    }

    [Fact]
    public async Task Grep_WithWildcardInPaths_IsRejected()
    {
        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, Pattern, paths: ["*.cs"], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.RejectedArgument, envelope.Error.Code);
        Assert.Equal("paths", envelope.Error.Detail["param"]);
    }

    [Fact]
    public async Task Grep_WithMagicInPaths_IsRejected()
    {
        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, Pattern, paths: [":(exclude)env"], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.RejectedArgument, envelope.Error.Code);
        Assert.Equal("paths", envelope.Error.Detail["param"]);
    }

    [Fact]
    public async Task Grep_WithMagicInGlobs_IsRejected()
    {
        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, Pattern, globs: [":(top)src"], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.RejectedArgument, envelope.Error.Code);
        Assert.Equal("globs", envelope.Error.Detail["param"]);
    }

    [Fact]
    public async Task Grep_WithParentSegmentInExcludePaths_IsRejected()
    {
        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, Pattern, excludePaths: ["../outside"], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.RejectedArgument, envelope.Error.Code);
        Assert.Equal("exclude_paths", envelope.Error.Detail["param"]);
    }

    [Fact]
    public async Task Grep_WithRootDotPath_MatchesEverything()
    {
        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, Pattern, paths: ["."], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(AllFiles, MatchedPaths(envelope));
    }

    [Fact]
    public async Task Grep_WithParenthesesInLiteralPath_FindsTheFile()
    {
        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, Pattern, paths: ["dir/(a,b).txt"], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(["dir/(a,b).txt"], MatchedPaths(envelope));
    }

    [Fact]
    public async Task Grep_FiltersApplied_ReportsCounts()
    {
        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, Pattern, globs: ["*.cs", "*.json"], excludePaths: ["env"], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(2, (int)envelope.FiltersApplied["globs_count"]);
        Assert.Equal(1, (int)envelope.FiltersApplied["exclude_paths_count"]);
    }

    #region Test Helpers

    private static List<string> MatchedPaths(ResultEnvelope envelope)
        => envelope.Results
            .Cast<GrepMatch>()
            .Select(match => match.Path)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

    #endregion
}