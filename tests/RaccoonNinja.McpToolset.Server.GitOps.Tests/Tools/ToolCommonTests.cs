using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RaccoonNinja.McpToolset.Server.GitOps.Errors;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Logging;
using RaccoonNinja.McpToolset.Server.GitOps.Metrics;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;
using RaccoonNinja.McpToolset.Server.GitOps.Security;
using RaccoonNinja.McpToolset.Server.GitOps.Tools;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Tools;

public class ToolCommonTests
{
    private static ToolCommon Build(
        IGitProcessRunner runner = null,
        IRepoRootResolver resolver = null,
        SessionMetrics metrics = null)
    {
        runner ??= Substitute.For<IGitProcessRunner>();
        resolver ??= Substitute.For<IRepoRootResolver>();
        return new ToolCommon(resolver, runner, metrics ?? new SessionMetrics(), NullLoggerFactory.Instance);
    }

    private static IGitProcessRunner RunnerReturning(int exitCode, byte[] stderr = null)
    {
        var runner = Substitute.For<IGitProcessRunner>();
        runner.RunAsync(
                Arg.Any<IList<string>>(), Arg.Any<IDictionary<string, string>>(), Arg.Any<string>(),
                Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RunResult
            {
                Argv = new List<string>(),
                ExitCode = exitCode,
                Stdout = System.Array.Empty<byte>(),
                Stderr = stderr ?? System.Array.Empty<byte>(),
                DurationMs = 1,
                Truncated = false,
            }));
        return runner;
    }

    [Fact]
    public async Task WrapAsync_TranslatesGitCheckExceptionToFailureEnvelope()
    {
        // Arrange
        var common = Build();
        var ctx = common.MakeContext("test");
        var holder = new RootHolder();
        holder.Set("/repo");

        // Act
        var envelope = await common.WrapAsync(ctx, holder, () =>
            throw new RejectedArgumentException("bad", new Dictionary<string, object> { ["param"] = "x" }));

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.RejectedArgument, envelope.Error.Code);
        Assert.Equal("/repo", envelope.RepoRoot);
    }

    [Fact]
    public async Task WrapAsync_WrapsUnexpectedExceptionAsGitCommandError()
    {
        // Arrange
        var common = Build();
        var ctx = common.MakeContext("test");

        // Act
        var envelope = await common.WrapAsync(ctx, new RootHolder(), () =>
            throw new System.InvalidOperationException("boom"));

        // Assert
        Assert.Equal(ErrorCodes.GitCommandError, envelope.Error.Code);
    }

    [Fact]
    public async Task ExecuteAsync_RecordsMetricsAndCallsRunner()
    {
        // Arrange
        var runner = Substitute.For<IGitProcessRunner>();
        runner.RunAsync(
                Arg.Any<IList<string>>(), Arg.Any<IDictionary<string, string>>(), Arg.Any<string>(),
                Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RunResult
            {
                Argv = new List<string>(),
                ExitCode = 0,
                Stdout = System.Array.Empty<byte>(),
                Stderr = System.Array.Empty<byte>(),
                DurationMs = 5,
                Truncated = false,
            }));

        var common = Build(runner: runner);
        var ctx = common.MakeContext("test");
        var intent = new GitIntent { Subcommand = "status", RepoRoot = Path.GetTempPath() };

        // Act
        var result = await common.ExecuteAsync(intent, ctx);

        // Assert
        Assert.Equal(0, result.ExitCode);
        await runner.Received(1).RunAsync(
            Arg.Any<IList<string>>(), Arg.Any<IDictionary<string, string>>(), Arg.Any<string>(),
            Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsGitCommandErrorWithExitCodeAndNoStderrInDetail()
    {
        // Arrange
        var runner = RunnerReturning(128, System.Text.Encoding.UTF8.GetBytes("fatal: bad revision"));
        var common = Build(runner: runner);
        var ctx = common.MakeContext("test");
        var intent = new GitIntent { Subcommand = "status", RepoRoot = Path.GetTempPath() };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<GitCommandException>(() => common.ExecuteAsync(intent, ctx));

        Assert.Equal(128, ex.Detail["git_exit_code"]);
        Assert.False(ex.Detail.ContainsKey(LogFields.StderrTail));
    }

    [Fact]
    public async Task ExecuteAsync_UsesStderrClassifierToMapKnownFailure()
    {
        // Arrange
        var runner = RunnerReturning(128, System.Text.Encoding.UTF8.GetBytes("classify me"));
        var common = Build(runner: runner);
        var ctx = common.MakeContext("test");
        var intent = new GitIntent { Subcommand = "grep", RepoRoot = Path.GetTempPath() };

        // Act & Assert
        await Assert.ThrowsAsync<PcreUnavailableException>(() => common.ExecuteAsync(
            intent,
            ctx,
            allowedExitCodes: new HashSet<int> { 0, 1 },
            stderrClassifier: _ => new PcreUnavailableException()));
    }

    [Fact]
    public async Task ExecuteAsync_FallsBackToGitCommandErrorWhenClassifierReturnsNull()
    {
        // Arrange
        var runner = RunnerReturning(128, System.Text.Encoding.UTF8.GetBytes("fatal: unrelated"));
        var common = Build(runner: runner);
        var ctx = common.MakeContext("test");
        var intent = new GitIntent { Subcommand = "grep", RepoRoot = Path.GetTempPath() };

        // Act & Assert
        await Assert.ThrowsAsync<GitCommandException>(() => common.ExecuteAsync(
            intent,
            ctx,
            allowedExitCodes: new HashSet<int> { 0, 1 },
            stderrClassifier: _ => null));
    }

    [Fact]
    public async Task WrapAsync_RecordsSingleErrorAndGitCommandErrorCounterOnGitFailure()
    {
        // Arrange
        var metrics = new SessionMetrics();
        var common = Build(runner: RunnerReturning(1), metrics: metrics);
        var ctx = common.MakeContext("git_test");

        // Act
        var envelope = await common.WrapAsync(ctx, new RootHolder(), async () =>
        {
            await common.ExecuteAsync(new GitIntent { Subcommand = "status", RepoRoot = Path.GetTempPath() }, ctx);
            return ToolCommon.SingleSuccess("unreachable", "/repo");
        });

        // Assert
        Assert.Equal(ErrorCodes.GitCommandError, envelope.Error.Code);
        var summary = metrics.Summary();
        var calls = (Dictionary<string, object>)summary["tool_calls_total"];
        Assert.Equal(1L, (long)calls["git_test:error"]);
        Assert.False(calls.ContainsKey("git_test:ok"));
        Assert.Equal(1L, summary["git_command_errors_total"]);
    }

    [Fact]
    public async Task WrapAsync_RecordsSingleOkOncePerCall()
    {
        // Arrange
        var metrics = new SessionMetrics();
        var common = Build(metrics: metrics);
        var ctx = common.MakeContext("git_test");

        // Act
        await common.WrapAsync(ctx, new RootHolder(), () =>
            Task.FromResult(ToolCommon.SingleSuccess("ok", "/repo")));

        // Assert
        var calls = (Dictionary<string, object>)metrics.Summary()["tool_calls_total"];
        Assert.Equal(1L, (long)calls["git_test:ok"]);
    }

    [Fact]
    public void ConfinePaths_ReturnsEmptyForNullPaths()
    {
        // Act & Assert
        Assert.Empty(ToolCommon.ConfinePaths(Path.GetTempPath(), null));
    }

    [Fact]
    public void ConfinePaths_ConfinesEachPathRelativeToRoot()
    {
        // Act
        var result = ToolCommon.ConfinePaths(Path.GetTempPath(), ["sub/file.txt"]);

        // Assert
        Assert.Equal(["sub/file.txt"], result);
    }

    [Fact]
    public void Make_ContextHasToolNameAndUniqueCallId()
    {
        // Arrange
        var common = Build();

        // Act
        var a = common.MakeContext("git_log");
        var b = common.MakeContext("git_log");

        // Assert
        Assert.Equal("git_log", a.Tool);
        Assert.NotEqual(a.CallId, b.CallId);
    }

    [Fact]
    public void SingleSuccess_WrapsPayloadInOneElementList()
    {
        // Act
        var env = ToolCommon.SingleSuccess("hi", "/repo");

        // Assert
        Assert.Single(env.Results, "hi");
    }

    [Fact]
    public void ListSuccess_CopiesItemsIntoResults()
    {
        // Act
        var env = ToolCommon.ListSuccess(new object[] { 1, 2, 3 }, "/repo");

        // Assert
        Assert.Equal(3, env.Count);
    }
}