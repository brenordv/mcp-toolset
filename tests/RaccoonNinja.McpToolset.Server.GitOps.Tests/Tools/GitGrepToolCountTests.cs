using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Server.GitOps.Errors;
using RaccoonNinja.McpToolset.Server.GitOps.Metrics;
using RaccoonNinja.McpToolset.Server.GitOps.Models;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;
using RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;
using RaccoonNinja.McpToolset.Server.GitOps.Tools;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Tools;

public sealed class GitGrepToolCountTests : IDisposable
{
    private const string Pattern = "hit";

    private static readonly string[] HitFiles = ["a.txt", "b.txt"];

    private readonly TempGitRepo _repo;
    private readonly GitGrepTool _tool;

    public GitGrepToolCountTests()
    {
        _repo = new TempGitRepo();
        _repo.WriteFile("a.txt", "hit\nhit hit\nmiss\n");
        _repo.WriteFile("b.txt", "hit\n");
        _repo.WriteFile("c.txt", "nothing\n");
        _repo.Run("add", ".");
        _repo.Run("commit", "-m", "add count tree");

        _tool = BuildTool(new GitProcessRunner(NullLogger<GitProcessRunner>.Instance), NullLoggerFactory.Instance);
    }

    public void Dispose() => _repo.Dispose();

    [Fact]
    public async Task Grep_CountOnly_ReturnsMatchingLineCountPerFile()
    {
        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, Pattern, countOnly: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(2, envelope.Count);
        Assert.Collection(
            envelope.Results.Cast<GrepFileCount>(),
            count => Assert.Equal(new GrepFileCount { Path = "a.txt", MatchCount = 2 }, count),
            count => Assert.Equal(new GrepFileCount { Path = "b.txt", MatchCount = 1 }, count));
        Assert.True((bool)envelope.FiltersApplied["count_only"]);
    }

    [Fact]
    public async Task Grep_CountOnly_WithRef_StripsRefPrefix()
    {
        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, Pattern, @ref: "HEAD", countOnly: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(HitFiles, envelope.Results.Cast<GrepFileCount>().Select(count => count.Path));
    }

    [Fact]
    public async Task Grep_CountOnly_WithMaxCount_IsRejected()
    {
        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, Pattern, maxCount: 1, countOnly: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.RejectedArgument, envelope.Error.Code);
        Assert.Equal("max_count", envelope.Error.Detail["param"]);
    }

    [Fact]
    public async Task Grep_CountOnly_WithColorUiAlways_ReturnsPlainPaths()
    {
        // Arrange
        _repo.Run("config", "color.ui", "always");

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, Pattern, countOnly: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(HitFiles, envelope.Results.Cast<GrepFileCount>().Select(count => count.Path));
    }

    [Fact]
    public async Task Grep_WithColorUiAlways_ReturnsMatches()
    {
        // Arrange
        _repo.Run("config", "color.ui", "always");

        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, Pattern, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(3, envelope.Count);
        Assert.All(envelope.Results.Cast<GrepMatch>(), match => Assert.Contains(match.Path, HitFiles));
    }

    [Fact]
    public async Task Grep_CountOnly_NoMatches_ReturnsEmptySuccess()
    {
        // Act
        var envelope = await _tool.InvokeAsync(_repo.RepoPath, "absent", countOnly: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Empty(envelope.Results);
    }

    [Fact]
    public async Task Grep_CountOnly_WithUnreadableOutput_LogsCountParseEmptyWarning()
    {
        // Arrange
        var loggerFactory = new RecordingLoggerFactory();
        var tool = BuildTool(new StdoutDroppingRunner(_ => true), loggerFactory);

        // Act
        var envelope = await tool.InvokeAsync(_repo.RepoPath, Pattern, countOnly: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Empty(envelope.Results);
        var warning = Assert.Single(loggerFactory.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Equal("count_parse_empty", warning.Message);
    }

    #region Test Helpers

    /// <summary>
    /// Build the tool under test over <paramref name="runner"/>, logging through <paramref name="loggerFactory"/>, with the
    /// real root resolver and ref verifier.
    /// </summary>
    /// <param name="runner">The runner every git process of the call goes through.</param>
    /// <param name="loggerFactory">The factory the call's log lines go to.</param>
    /// <returns>The tool.</returns>
    private static GitGrepTool BuildTool(IGitProcessRunner runner, ILoggerFactory loggerFactory)
    {
        var common = new ToolCommon(
            new RepoRootResolver(TestGit.Executable),
            runner,
            new SessionMetrics(),
            loggerFactory,
            TestGit.Executable);
        return new GitGrepTool(common, new RefVerifier(TestGit.Executable));
    }

    #endregion
}