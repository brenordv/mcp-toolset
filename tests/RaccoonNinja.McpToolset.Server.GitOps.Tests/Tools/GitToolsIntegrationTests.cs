using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Server.GitOps.Metrics;
using RaccoonNinja.McpToolset.Server.GitOps.Models;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;
using RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;
using RaccoonNinja.McpToolset.Server.GitOps.Tools;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Tools;

[Collection(nameof(GitRepoCollection))]
public class GitToolsIntegrationTests
{
    private readonly GitRepoFixture _fixture;
    private readonly ToolCommon _common;
    private readonly IRefVerifier _refVerifier;

    public GitToolsIntegrationTests(GitRepoFixture fixture)
    {
        _fixture = fixture;
        var resolver = new RepoRootResolver();
        var runner = new GitProcessRunner(NullLogger<GitProcessRunner>.Instance);
        var metrics = new SessionMetrics();
        _common = new ToolCommon(resolver, runner, metrics, NullLoggerFactory.Instance);
        _refVerifier = new RefVerifier();
    }

    [Fact]
    public async Task GitStatusTool_ReturnsStatusForCleanFixtureRepo()
    {
        // Arrange
        var tool = new GitStatusTool(_common);

        // Act
        var envelope = await tool.InvokeAsync(_fixture.RepoPath);

        // Assert
        Assert.Null(envelope.Error);
        Assert.NotEmpty(envelope.Results);
    }

    [Fact]
    public async Task GitLogTool_ReturnsCommitsForFixture()
    {
        // Arrange
        var tool = new GitLogTool(_common, _refVerifier);

        // Act
        var envelope = await tool.InvokeAsync(_fixture.RepoPath);

        // Assert
        Assert.Null(envelope.Error);
        Assert.True(envelope.Count >= 3);
    }

    [Fact]
    public async Task GitLogTool_RespectsMaxCount()
    {
        // Arrange
        var tool = new GitLogTool(_common, _refVerifier);

        // Act
        var envelope = await tool.InvokeAsync(_fixture.RepoPath, maxCount: 1);

        // Assert
        Assert.Equal(1, envelope.Count);
    }

    [Fact]
    public async Task GitDiffTool_StatOnlyReturnsNumstat()
    {
        // Arrange
        var tool = new GitDiffTool(_common, _refVerifier);

        // Act
        var envelope = await tool.InvokeAsync(_fixture.RepoPath, fromRef: "HEAD~1", toRef: "HEAD", statOnly: true);

        // Assert
        Assert.Null(envelope.Error);
    }

    [Fact]
    public async Task GitDiffTool_StatOnlyReportsAccurateChangeType()
    {
        // Arrange
        var tool = new GitDiffTool(_common, _refVerifier);

        // Act
        var envelope = await tool.InvokeAsync(_fixture.RepoPath, fromRef: "HEAD~1", toRef: "HEAD", statOnly: true);

        // Assert
        Assert.Null(envelope.Error);
        var result = (DiffResult)envelope.Results[0];
        Assert.Equal(ChangeType.Modified, result.Files.Single(file => file.Path == "alpha.txt").ChangeType);
        Assert.DoesNotContain(result.Files, file => file.ChangeType == ChangeType.Unknown);
    }

    [Fact]
    public async Task GitShowTool_ReturnsCommitAndFiles()
    {
        // Arrange
        var tool = new GitShowTool(_common, _refVerifier);

        // Act
        var envelope = await tool.InvokeAsync(_fixture.RepoPath, @ref: "HEAD");

        // Assert
        Assert.Null(envelope.Error);
        var payload = (System.Collections.Generic.IDictionary<string, object>)envelope.Results[0];
        Assert.NotNull(payload["commit"]);
        Assert.NotNull(payload["files"]);
    }

    [Fact]
    public async Task GitBlameTool_ReturnsLinesForFile()
    {
        // Arrange
        var tool = new GitBlameTool(_common, _refVerifier);

        // Act
        var envelope = await tool.InvokeAsync(_fixture.RepoPath, "alpha.txt");

        // Assert
        Assert.Null(envelope.Error);
        Assert.True(envelope.Count >= 3);
    }

    [Fact]
    public async Task GitBranchListTool_ReturnsMain()
    {
        // Arrange
        var tool = new GitBranchListTool(_common);

        // Act
        var envelope = await tool.InvokeAsync(_fixture.RepoPath);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Contains(envelope.Results.Cast<RaccoonNinja.McpToolset.Server.GitOps.Models.Branch>(),
            b => b.Name == "main");
    }

    [Fact]
    public async Task GitLsFilesTool_ListsThreeTrackedFiles()
    {
        // Arrange
        var tool = new GitLsFilesTool(_common);

        // Act
        var envelope = await tool.InvokeAsync(_fixture.RepoPath);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Contains("README.md", envelope.Results);
        Assert.Contains("alpha.txt", envelope.Results);
        Assert.Contains("beta.txt", envelope.Results);
    }

    [Fact]
    public async Task GitGrepTool_FindsTokenInFixtureFiles()
    {
        // Arrange
        var tool = new GitGrepTool(_common, _refVerifier);

        // Act
        var envelope = await tool.InvokeAsync(_fixture.RepoPath, "alpha");

        // Assert
        Assert.Null(envelope.Error);
        Assert.NotEmpty(envelope.Results);
    }

    [Fact]
    public async Task GitGrepTool_EchoesRegexEngineInFiltersApplied()
    {
        // Arrange
        var tool = new GitGrepTool(_common, _refVerifier);

        // Act & Assert
        var fixedEnvelope = await tool.InvokeAsync(_fixture.RepoPath, "alpha", fixedString: true);
        Assert.Equal("fixed", (string)fixedEnvelope.FiltersApplied["regex_engine"]);

        var regexEnvelope = await tool.InvokeAsync(_fixture.RepoPath, "alpha", fixedString: false);
        Assert.Equal("perl", (string)regexEnvelope.FiltersApplied["regex_engine"]);
    }

    [Fact]
    public async Task GitGrepTool_RegexModeUsesPcreOrReportsUnavailable()
    {
        // Arrange
        var tool = new GitGrepTool(_common, _refVerifier);

        // Act
        var envelope = await tool.InvokeAsync(_fixture.RepoPath, @"al\w+a", fixedString: false);

        // Assert
        if (envelope.Error is null)
        {
            Assert.NotEmpty(envelope.Results);
        }
        else
        {
            Assert.Equal(RaccoonNinja.McpToolset.Server.GitOps.Errors.ErrorCodes.PcreUnavailable, envelope.Error.Code);
        }
    }

    [Fact]
    public async Task GitGrepTool_EmptyMatchReturnsEmptyWithoutError()
    {
        // Arrange
        var tool = new GitGrepTool(_common, _refVerifier);

        // Act
        var envelope = await tool.InvokeAsync(_fixture.RepoPath, "tHisTokenWillNeverEverMatch_xqz_xqz");

        // Assert
        Assert.Null(envelope.Error);
        Assert.Empty(envelope.Results);
    }

    [Fact]
    public async Task GitStashListTool_ReturnsEmptyEnvelopeForStashFreeRepo()
    {
        // Arrange
        var tool = new GitStashListTool(_common);

        // Act
        var envelope = await tool.InvokeAsync(_fixture.RepoPath);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Empty(envelope.Results);
    }

    [Fact]
    public async Task GitReflogTool_ReturnsEntriesForFixtureHistory()
    {
        // Arrange
        var tool = new GitReflogTool(_common);

        // Act
        var envelope = await tool.InvokeAsync(_fixture.RepoPath);

        // Assert
        Assert.Null(envelope.Error);
        Assert.NotEmpty(envelope.Results);
    }

    [Fact]
    public async Task GitBlameTool_ReturnsGitCommandErrorForUntrackedPath()
    {
        // Arrange
        var tool = new GitBlameTool(_common, _refVerifier);

        // Act
        var envelope = await tool.InvokeAsync(_fixture.RepoPath, "does-not-exist.txt");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(RaccoonNinja.McpToolset.Server.GitOps.Errors.ErrorCodes.GitCommandError, envelope.Error.Code);
        Assert.True(envelope.Error.Detail.ContainsKey("git_exit_code"));
        Assert.False(envelope.Error.Detail.ContainsKey("stderr_tail"));
    }

    [Fact]
    public async Task GitStashShowTool_RejectsNegativeIndex()
    {
        // Arrange
        var tool = new GitStashShowTool(_common);

        // Act
        var envelope = await tool.InvokeAsync(_fixture.RepoPath, index: -1);

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(RaccoonNinja.McpToolset.Server.GitOps.Errors.ErrorCodes.RejectedArgument, envelope.Error.Code);
    }

    [Fact]
    public async Task GitStatusTool_ReturnsFailureEnvelopeForNonRepoCwd()
    {
        // Arrange
        var nonRepo = Path.Combine(Path.GetTempPath(), "no-repo-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(nonRepo);
        try
        {
            var tool = new GitStatusTool(_common);

            // Act
            var envelope = await tool.InvokeAsync(nonRepo);

            // Assert
            Assert.NotNull(envelope.Error);
            Assert.Equal(RaccoonNinja.McpToolset.Server.GitOps.Errors.ErrorCodes.NotAGitRepository, envelope.Error.Code);
        }
        finally
        {
            Directory.Delete(nonRepo, true);
        }
    }

    [Fact]
    public async Task GitToolsEnvelope_IsSerializableToJson()
    {
        // Arrange
        var tool = new GitStatusTool(_common);
        var envelope = await tool.InvokeAsync(_fixture.RepoPath);

        // Act
        var json = JsonSerializer.Serialize(envelope);

        // Assert
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("repo_root", out _));
        Assert.True(doc.RootElement.TryGetProperty("results", out _));
    }
}