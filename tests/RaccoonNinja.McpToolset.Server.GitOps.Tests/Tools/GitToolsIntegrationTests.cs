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
    public async Task GitGrepTool_Finds_Token_At_Ref_With_Prefix_Stripped()
    {
        var tool = new GitGrepTool(_common, _refVerifier);
        var envelope = await tool.InvokeAsync(_fixture.RepoPath, "alpha", @ref: "HEAD");
        Assert.Null(envelope.Error);
        Assert.NotEmpty(envelope.Results);
        var match = (GrepMatch)envelope.Results[0];
        Assert.Equal("alpha.txt", match.Path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task GitGrepTool_Rejects_Null_Or_Empty_Pattern(string pattern)
    {
        var tool = new GitGrepTool(_common, _refVerifier);
        var envelope = await tool.InvokeAsync(_fixture.RepoPath, pattern);
        Assert.NotNull(envelope.Error);
        Assert.Equal(RaccoonNinja.McpToolset.Server.GitOps.Errors.ErrorCodes.RejectedArgument, envelope.Error.Code);
        Assert.Equal("pattern", envelope.Error.Detail["param"]);
    }

    [Fact]
    public async Task GitGrepTool_Accepts_Whitespace_Pattern_As_Valid_Search()
    {
        var tool = new GitGrepTool(_common, _refVerifier);
        var envelope = await tool.InvokeAsync(_fixture.RepoPath, " ");
        Assert.Null(envelope.Error);
        Assert.NotEmpty(envelope.Results);
    }

    [Fact]
    public async Task GitGrepTool_Rejects_Pattern_Beginning_With_Dash_Naming_Pattern_Param()
    {
        var tool = new GitGrepTool(_common, _refVerifier);
        var envelope = await tool.InvokeAsync(_fixture.RepoPath, "-verbose");
        Assert.NotNull(envelope.Error);
        Assert.Equal(RaccoonNinja.McpToolset.Server.GitOps.Errors.ErrorCodes.RejectedArgument, envelope.Error.Code);
        Assert.Equal("pattern", envelope.Error.Detail["param"]);
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

    [Theory]
    [InlineData("HEAD~2..HEAD")]
    [InlineData("HEAD~2...HEAD")]
    public async Task GitLogTool_Accepts_Range_Expression_And_Returns_Branch_Commits(string range)
    {
        var tool = new GitLogTool(_common, _refVerifier);
        var envelope = await tool.InvokeAsync(_fixture.RepoPath, @ref: range);
        Assert.Null(envelope.Error);
        Assert.Equal(2, envelope.Count);
    }

    [Fact]
    public async Task GitLogTool_Rejects_Malformed_Range()
    {
        var tool = new GitLogTool(_common, _refVerifier);
        var envelope = await tool.InvokeAsync(_fixture.RepoPath, @ref: "A....B");
        Assert.NotNull(envelope.Error);
        Assert.Equal(RaccoonNinja.McpToolset.Server.GitOps.Errors.ErrorCodes.RejectedArgument, envelope.Error.Code);
    }

    [Fact]
    public async Task GitDiffTool_TwoDot_Range_In_FromRef_Matches_Two_Ref_File_Set()
    {
        var tool = new GitDiffTool(_common, _refVerifier);

        var rangeEnvelope = await tool.InvokeAsync(_fixture.RepoPath, fromRef: "HEAD~2..HEAD");
        var twoRefEnvelope = await tool.InvokeAsync(_fixture.RepoPath, fromRef: "HEAD~2", toRef: "HEAD");

        Assert.Null(rangeEnvelope.Error);
        Assert.Null(twoRefEnvelope.Error);
        var rangePaths = ((DiffResult)rangeEnvelope.Results[0]).Files.Select(f => f.Path).OrderBy(p => p);
        var twoRefPaths = ((DiffResult)twoRefEnvelope.Results[0]).Files.Select(f => f.Path).OrderBy(p => p);
        Assert.Equal(twoRefPaths, rangePaths);
    }

    [Fact]
    public async Task GitDiffTool_ThreeDot_Range_In_FromRef_Runs_Without_Error()
    {
        var tool = new GitDiffTool(_common, _refVerifier);
        var envelope = await tool.InvokeAsync(_fixture.RepoPath, fromRef: "HEAD~1...HEAD");
        Assert.Null(envelope.Error);
    }

    [Fact]
    public async Task GitDiffTool_Range_FromRef_With_Default_ToRef_Does_Not_Crash()
    {
        var tool = new GitDiffTool(_common, _refVerifier);
        var envelope = await tool.InvokeAsync(_fixture.RepoPath, fromRef: "HEAD~1..HEAD");
        Assert.Null(envelope.Error);
    }

    [Theory]
    [InlineData("HEAD~1..HEAD", "HEAD", "from_ref")]
    [InlineData("HEAD", "HEAD~1..HEAD", "to_ref")]
    public async Task GitDiffTool_Rejects_Range_Combined_With_Second_Ref(string fromRef, string toRef, string param)
    {
        var tool = new GitDiffTool(_common, _refVerifier);
        var envelope = await tool.InvokeAsync(_fixture.RepoPath, fromRef: fromRef, toRef: toRef);
        Assert.NotNull(envelope.Error);
        Assert.Equal(RaccoonNinja.McpToolset.Server.GitOps.Errors.ErrorCodes.RejectedArgument, envelope.Error.Code);
        Assert.Equal(param, envelope.Error.Detail["param"]);
    }

    [Fact]
    public async Task GitLogTool_Rejects_Range_Side_Beginning_With_Dash()
    {
        var tool = new GitLogTool(_common, _refVerifier);
        var envelope = await tool.InvokeAsync(_fixture.RepoPath, @ref: "-x..HEAD");
        Assert.NotNull(envelope.Error);
        Assert.Equal(RaccoonNinja.McpToolset.Server.GitOps.Errors.ErrorCodes.RejectedArgument, envelope.Error.Code);
    }

    [Fact]
    public async Task GitLogTool_Accepts_Reflog_Revision_As_Range_Side()
    {
        var tool = new GitLogTool(_common, _refVerifier);
        var envelope = await tool.InvokeAsync(_fixture.RepoPath, @ref: "HEAD@{0}..HEAD");
        Assert.Null(envelope.Error);
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