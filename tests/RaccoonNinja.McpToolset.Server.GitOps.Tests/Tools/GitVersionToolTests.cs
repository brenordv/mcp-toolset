using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Server.GitOps.Errors;
using RaccoonNinja.McpToolset.Server.GitOps.Metrics;
using RaccoonNinja.McpToolset.Server.GitOps.Models;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;
using RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;
using RaccoonNinja.McpToolset.Server.GitOps.Tools;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Tools;

public sealed class GitVersionToolTests
{
    private const string StartupVersion = "2.56.0";
    private const int StartupGitExitCode = 128;
    private const string NotInstalledMessage = "no git version was read when the server started: git was not found on PATH or could not be started";
    private const string TimeoutMessage = "no git version was read when the server started: git version did not exit in time";
    private const string CommandErrorMessage = "no git version was read when the server started: git version exited non-zero or printed an unexpected line";

    [Fact]
    public async Task Version_ReadAtStartup_ReturnsItWithNoRepoRoot()
    {
        // Arrange
        var tool = BuildTool(new GitVersionProbe { Version = StartupVersion });

        // Act
        var envelope = await tool.InvokeAsync();

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(StartupVersion, Assert.IsType<GitVersionResult>(Assert.Single(envelope.Results)).Version);
        Assert.Null(envelope.RepoRoot);
    }

    [Theory]
    [InlineData(ErrorCodes.GitNotInstalled, NotInstalledMessage)]
    [InlineData(ErrorCodes.GitTimeout, TimeoutMessage)]
    [InlineData(ErrorCodes.GitCommandError, CommandErrorMessage)]
    public async Task Version_NotReadAtStartup_ReturnsTheStartupErrorCode(string errorCode, string message)
    {
        // Arrange
        var tool = BuildTool(new GitVersionProbe { ErrorCode = errorCode });

        // Act
        var envelope = await tool.InvokeAsync();

        // Assert
        Assert.Equal(errorCode, envelope.Error.Code);
        Assert.Equal(message, envelope.Error.Message);
        Assert.Empty(envelope.Results);
    }

    [Fact]
    public async Task Version_GitNotFoundAtStartup_NamesTheExecutable()
    {
        // Arrange
        var tool = BuildTool(new GitVersionProbe { ErrorCode = ErrorCodes.GitNotInstalled });

        // Act
        var envelope = await tool.InvokeAsync();

        // Assert
        Assert.Equal(TestGit.DisplayName, envelope.Error.Detail[TestGit.NotInstalledDetailKey]);
    }

    [Fact]
    public async Task Version_GitExitedNonZeroAtStartup_ReportsTheExitCode()
    {
        // Arrange
        var tool = BuildTool(new GitVersionProbe { ErrorCode = ErrorCodes.GitCommandError, GitExitCode = StartupGitExitCode });

        // Act
        var envelope = await tool.InvokeAsync();

        // Assert
        Assert.Equal(StartupGitExitCode, envelope.Error.Detail["git_exit_code"]);
    }

    #region Test Helpers

    /// <summary>Build the tool under test over <paramref name="probe"/>, with a runner that fails any git process.</summary>
    /// <param name="probe">What the startup call found.</param>
    /// <returns>The tool.</returns>
    private static GitVersionTool BuildTool(GitVersionProbe probe)
    {
        var common = new ToolCommon(
            new RepoRootResolver(TestGit.Executable),
            new NoGitRunner(),
            new SessionMetrics(),
            NullLoggerFactory.Instance,
            TestGit.Executable);
        return new GitVersionTool(common, probe);
    }

    /// <summary>A runner that throws for every process, so a <c>git_version</c> call that started git would fail.</summary>
    private sealed class NoGitRunner : IGitProcessRunner
    {
        public Task<RunResult> RunAsync(
            IList<string> argv,
            IDictionary<string, string> env,
            string workingDirectory,
            int? timeoutMs = null,
            int? outputCapBytes = null,
            int? callId = null,
            byte[] standardInput = null,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("git_version started a git process");
    }

    #endregion
}