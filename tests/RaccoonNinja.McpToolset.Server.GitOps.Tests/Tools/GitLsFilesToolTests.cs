using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Server.GitOps.Envelope;
using RaccoonNinja.McpToolset.Server.GitOps.Errors;
using RaccoonNinja.McpToolset.Server.GitOps.Metrics;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;
using RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;
using RaccoonNinja.McpToolset.Server.GitOps.Tools;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Tools;

public sealed class GitLsFilesToolTests : IDisposable
{
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
    private readonly GitLsFilesTool _tool;

    public GitLsFilesToolTests()
    {
        _repo = new TempGitRepo();
        foreach (var path in AllFiles)
        {
            _repo.WriteFile(path, $"name = {path}\n");
        }

        _repo.Run("add", ".");
        _repo.Run("commit", "-m", "add pathspec tree");

        var common = new ToolCommon(
            new RepoRootResolver(TestGit.Executable),
            new GitProcessRunner(NullLogger<GitProcessRunner>.Instance),
            new SessionMetrics(),
            NullLoggerFactory.Instance,
            TestGit.Executable);
        _tool = new GitLsFilesTool(common);
    }

    public void Dispose() => _repo.Dispose();

    [Fact]
    public async Task LsFiles_WithGlobs_ListsTrackedFilesOfThatShape()
    {
        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, globs: ["*.json"], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(["nested/x.json", "root.json"], ListedPaths(envelope));
    }

    [Fact]
    public async Task LsFiles_WithExcludePaths_DropsSubtree()
    {
        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, excludePaths: ["crates"], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var paths = ListedPaths(envelope);
        Assert.Equal(AllFiles.Length - 3, paths.Count);
        Assert.DoesNotContain(paths, path => path.StartsWith("crates/", StringComparison.Ordinal));
        Assert.All(paths, path => Assert.Contains(path, AllFiles));
    }

    [Fact]
    public async Task LsFiles_WithWildcardInPaths_IsRejected()
    {
        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, paths: ["*.json"], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.RejectedArgument, envelope.Error.Code);
        Assert.Equal("paths", envelope.Error.Detail["param"]);
    }

    #region Test Helpers

    private static List<string> ListedPaths(ResultEnvelope envelope)
        => envelope.Results
            .Cast<string>()
            .Order(StringComparer.Ordinal)
            .ToList();

    #endregion
}