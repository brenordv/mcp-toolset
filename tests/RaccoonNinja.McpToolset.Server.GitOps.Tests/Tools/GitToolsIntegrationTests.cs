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
    public async Task GitStatusTool_Returns_Status_For_Clean_Fixture_Repo()
    {
        var tool = new GitStatusTool(_common);
        var envelope = await tool.InvokeAsync(_fixture.RepoPath);
        Assert.Null(envelope.Error);
        Assert.NotEmpty(envelope.Results);
    }

    [Fact]
    public async Task GitLogTool_Returns_Commits_For_Fixture()
    {
        var tool = new GitLogTool(_common, _refVerifier);
        var envelope = await tool.InvokeAsync(_fixture.RepoPath);
        Assert.Null(envelope.Error);
        Assert.True(envelope.Count >= 3);
    }

    [Fact]
    public async Task GitLogTool_Respects_MaxCount()
    {
        var tool = new GitLogTool(_common, _refVerifier);
        var envelope = await tool.InvokeAsync(_fixture.RepoPath, maxCount: 1);
        Assert.Equal(1, envelope.Count);
    }

    [Fact]
    public async Task GitDiffTool_Stat_Only_Returns_Numstat()
    {
        var tool = new GitDiffTool(_common, _refVerifier);
        var envelope = await tool.InvokeAsync(_fixture.RepoPath, fromRef: "HEAD~1", toRef: "HEAD", statOnly: true);
        Assert.Null(envelope.Error);
    }

    [Fact]
    public async Task GitDiffTool_Stat_Only_Reports_Accurate_Change_Type()
    {
        var tool = new GitDiffTool(_common, _refVerifier);
        var envelope = await tool.InvokeAsync(_fixture.RepoPath, fromRef: "HEAD~1", toRef: "HEAD", statOnly: true);

        Assert.Null(envelope.Error);
        var result = (DiffResult)envelope.Results[0];
        Assert.Equal(ChangeType.Modified, result.Files.Single(file => file.Path == "alpha.txt").ChangeType);
        Assert.DoesNotContain(result.Files, file => file.ChangeType == ChangeType.Unknown);
    }

    [Fact]
    public async Task GitShowTool_Returns_Commit_And_Files()
    {
        var tool = new GitShowTool(_common, _refVerifier);
        var envelope = await tool.InvokeAsync(_fixture.RepoPath, @ref: "HEAD");
        Assert.Null(envelope.Error);
        var payload = (System.Collections.Generic.IDictionary<string, object>)envelope.Results[0];
        Assert.NotNull(payload["commit"]);
        Assert.NotNull(payload["files"]);
    }

    [Fact]
    public async Task GitBlameTool_Returns_Lines_For_File()
    {
        var tool = new GitBlameTool(_common, _refVerifier);
        var envelope = await tool.InvokeAsync(_fixture.RepoPath, "alpha.txt");
        Assert.Null(envelope.Error);
        Assert.True(envelope.Count >= 3);
    }

    [Fact]
    public async Task GitBranchListTool_Returns_Main()
    {
        var tool = new GitBranchListTool(_common);
        var envelope = await tool.InvokeAsync(_fixture.RepoPath);
        Assert.Null(envelope.Error);
        Assert.Contains(envelope.Results.Cast<RaccoonNinja.McpToolset.Server.GitOps.Models.Branch>(),
            b => b.Name == "main");
    }

    [Fact]
    public async Task GitLsFilesTool_Lists_Three_Tracked_Files()
    {
        var tool = new GitLsFilesTool(_common);
        var envelope = await tool.InvokeAsync(_fixture.RepoPath);
        Assert.Null(envelope.Error);
        Assert.Contains("README.md", envelope.Results);
        Assert.Contains("alpha.txt", envelope.Results);
        Assert.Contains("beta.txt", envelope.Results);
    }

    [Fact]
    public async Task GitGrepTool_Finds_Token_In_Fixture_Files()
    {
        var tool = new GitGrepTool(_common, _refVerifier);
        var envelope = await tool.InvokeAsync(_fixture.RepoPath, "alpha");
        Assert.Null(envelope.Error);
        Assert.NotEmpty(envelope.Results);
    }

    [Fact]
    public async Task GitGrepTool_Echoes_Regex_Engine_In_Filters_Applied()
    {
        var tool = new GitGrepTool(_common, _refVerifier);

        var fixedEnvelope = await tool.InvokeAsync(_fixture.RepoPath, "alpha", fixedString: true);
        Assert.Equal("fixed", (string)fixedEnvelope.FiltersApplied["regex_engine"]);

        var regexEnvelope = await tool.InvokeAsync(_fixture.RepoPath, "alpha", fixedString: false);
        Assert.Equal("perl", (string)regexEnvelope.FiltersApplied["regex_engine"]);
    }

    [Fact]
    public async Task GitGrepTool_Regex_Mode_Uses_Pcre_Or_Reports_Unavailable()
    {
        var tool = new GitGrepTool(_common, _refVerifier);

        // '\w' is a PCRE class with no POSIX BRE/ERE equivalent: under -P it matches "alpha";
        // under the old silent BRE default it matched nothing. The guarantee under test is that
        // the regex path is never a silent miss: Either it matches, or it fails loudly when the
        // host git lacks PCRE2.
        var envelope = await tool.InvokeAsync(_fixture.RepoPath, @"al\w+a", fixedString: false);

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
    public async Task GitGrepTool_Empty_Match_Returns_Empty_Without_Error()
    {
        var tool = new GitGrepTool(_common, _refVerifier);
        var envelope = await tool.InvokeAsync(_fixture.RepoPath, "tHisTokenWillNeverEverMatch_xqz_xqz");
        Assert.Null(envelope.Error);
        Assert.Empty(envelope.Results);
    }

    [Fact]
    public async Task GitStashListTool_Returns_Empty_Envelope_For_Stash_Free_Repo()
    {
        var tool = new GitStashListTool(_common);
        var envelope = await tool.InvokeAsync(_fixture.RepoPath);
        Assert.Null(envelope.Error);
        Assert.Empty(envelope.Results);
    }

    [Fact]
    public async Task GitReflogTool_Returns_Entries_For_Fixture_History()
    {
        var tool = new GitReflogTool(_common);
        var envelope = await tool.InvokeAsync(_fixture.RepoPath);
        Assert.Null(envelope.Error);
        Assert.NotEmpty(envelope.Results);
    }

    [Fact]
    public async Task GitBlameTool_Returns_GitCommandError_For_Untracked_Path()
    {
        var tool = new GitBlameTool(_common, _refVerifier);
        var envelope = await tool.InvokeAsync(_fixture.RepoPath, "does-not-exist.txt");
        Assert.NotNull(envelope.Error);
        Assert.Equal(RaccoonNinja.McpToolset.Server.GitOps.Errors.ErrorCodes.GitCommandError, envelope.Error.Code);
        Assert.True(envelope.Error.Detail.ContainsKey("git_exit_code"));
        Assert.False(envelope.Error.Detail.ContainsKey("stderr_tail"));
    }

    [Fact]
    public async Task GitStashShowTool_Rejects_Negative_Index()
    {
        var tool = new GitStashShowTool(_common);
        var envelope = await tool.InvokeAsync(_fixture.RepoPath, index: -1);
        Assert.NotNull(envelope.Error);
        Assert.Equal(RaccoonNinja.McpToolset.Server.GitOps.Errors.ErrorCodes.RejectedArgument, envelope.Error.Code);
    }

    [Fact]
    public async Task GitStatusTool_Returns_Failure_Envelope_For_NonRepo_Cwd()
    {
        var nonRepo = Path.Combine(Path.GetTempPath(), "no-repo-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(nonRepo);
        try
        {
            var tool = new GitStatusTool(_common);
            var envelope = await tool.InvokeAsync(nonRepo);
            Assert.NotNull(envelope.Error);
            Assert.Equal(RaccoonNinja.McpToolset.Server.GitOps.Errors.ErrorCodes.NotAGitRepository, envelope.Error.Code);
        }
        finally
        {
            Directory.Delete(nonRepo, true);
        }
    }

    [Fact]
    public async Task GitToolsEnvelope_Is_Serializable_To_Json()
    {
        var tool = new GitStatusTool(_common);
        var envelope = await tool.InvokeAsync(_fixture.RepoPath);
        var json = JsonSerializer.Serialize(envelope);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("repo_root", out _));
        Assert.True(doc.RootElement.TryGetProperty("results", out _));
    }
}