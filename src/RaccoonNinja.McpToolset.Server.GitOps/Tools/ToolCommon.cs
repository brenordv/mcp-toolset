using Microsoft.Extensions.Logging;
using RaccoonNinja.McpToolset.Server.GitOps.Envelope;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Logging;
using RaccoonNinja.McpToolset.Server.GitOps.Metrics;
using RaccoonNinja.McpToolset.Server.GitOps.Parsers;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;
using RaccoonNinja.McpToolset.Server.GitOps.Security;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tools;

/// <summary>
/// Shared per-tool helpers: root resolution, hardened execution, envelope wrapping.
/// Mirrors <c>tools/common.py</c>.
/// </summary>
public sealed class ToolCommon(
    IRepoRootResolver resolver,
    IGitProcessRunner runner,
    SessionMetrics metrics,
    ILoggerFactory loggerFactory)
{
    public CallContext MakeContext(string tool)
        => new(tool, loggerFactory.CreateLogger(tool));

    public async Task<string> ResolveAndLogAsync(string cwd, CallContext ctx, RootHolder holder, CancellationToken cancellationToken = default)
    {
        var resolution = await resolver.ResolveAsync(cwd, cancellationToken).ConfigureAwait(false);
        ctx.Log(
            LogLevel.Debug,
            resolution.CacheHit ? "cache_hit" : "cache_miss",
            extras: new Dictionary<string, object> { [LogFields.CacheHit] = resolution.CacheHit });
        metrics.RecordCache(resolution.CacheHit);
        holder?.Set(resolution.Root);
        return resolution.Root;
    }

    public async Task<RunResult> ExecuteAsync(
        GitIntent intent,
        CallContext ctx,
        int? timeoutMs = null,
        int? outputCapBytes = null,
        IReadOnlySet<int> allowedExitCodes = null,
        Func<string, GitCheckException> stderrClassifier = null,
        CancellationToken cancellationToken = default)
    {
        var (argv, env) = GitCommandBuilder.Build(intent);
        var masked = GitCommandBuilder.MaskedForLog(argv, intent);
        ctx.Log(
            LogLevel.Debug,
            "argv_built",
            message: string.Join(' ', masked),
            extras: new Dictionary<string, object> { [LogFields.ArgvLen] = argv.Count });

        RunResult result;
        try
        {
            result = await runner.RunAsync(argv, env, intent.RepoRoot, timeoutMs, outputCapBytes, ctx.CallId, cancellationToken).ConfigureAwait(false);
        }
        catch (GitTimeoutException)
        {
            // RecordTimeout is a distinct counter; the per-call outcome is recorded once by WrapAsync.
            metrics.RecordTimeout();
            throw;
        }

        metrics.RecordDurationMs(result.DurationMs);
        if (result.Truncated) metrics.RecordTruncation();

        var allowed = allowedExitCodes ?? OkOnly;
        if (allowed.Contains(result.ExitCode))
        {
            ctx.Log(
                LogLevel.Debug,
                "subprocess_exit",
                extras: new Dictionary<string, object>
                {
                    [LogFields.GitExitCode] = result.ExitCode,
                    [LogFields.DurationMs] = result.DurationMs,
                    [LogFields.Truncated] = result.Truncated,
                });
            return result;
        }

        // Disallowed exit: surface a domain error. An optional classifier may recognize a known,
        // user-data-free stderr signature (e.g. a git built without PCRE2) and map it to a more
        // specific code; otherwise the failure is the generic GitCommandError. The scrubbed stderr
        // tail goes to the LOG ONLY (it can echo user-controlled paths/refs); the client-facing
        // envelope detail carries the non-sensitive exit code alone. Logged at Debug. The single
        // Error record is WrapAsync's tool_error, which carries the exit code so it survives at the
        // default level.
        var domainError = ClassifyDisallowedExit(intent, result, stderrClassifier);
        ctx.Log(
            LogLevel.Debug,
            "subprocess_exit",
            extras: new Dictionary<string, object>
            {
                [LogFields.ErrorCode] = domainError.Code,
                [LogFields.GitExitCode] = result.ExitCode,
                [LogFields.DurationMs] = result.DurationMs,
                [LogFields.StderrTail] = LogScrubbing.ScrubStderrTail(result.Stderr),
            });
        metrics.RecordGitCommandError();
        throw domainError;
    }

    /// <summary>
    /// Map a disallowed git exit to a domain exception. A caller-supplied
    /// <paramref name="stderrClassifier"/> may recognize a specific, non-sensitive stderr
    /// signature; otherwise the failure is a generic <see cref="GitCommandException"/> carrying
    /// only the exit code.
    /// </summary>
    /// <param name="intent">The intent whose subcommand names the failed git invocation.</param>
    /// <param name="result">The completed run carrying the exit code and raw stderr.</param>
    /// <param name="stderrClassifier">Optional decoded-stderr classifier; may return <c>null</c>.</param>
    /// <returns>The domain exception to throw for this disallowed exit.</returns>
    private static GitCheckException ClassifyDisallowedExit(
        GitIntent intent,
        RunResult result,
        Func<string, GitCheckException> stderrClassifier)
    {
        var classified = stderrClassifier?.Invoke(TextDecoding.Decode(result.Stderr));
        return classified ?? new GitCommandException(
            $"git {intent.Subcommand} failed (exit {result.ExitCode})",
            new Dictionary<string, object> { [LogFields.GitExitCode] = result.ExitCode });
    }

    private static readonly HashSet<int> OkOnly = [0];

    /// <summary>Confine each user-supplied path under <paramref name="root"/>, yielding safe repo-relative pathspecs.</summary>
    /// <param name="root">The resolved repository root.</param>
    /// <param name="paths">The user-supplied paths; a null array yields an empty list.</param>
    /// <returns>The confined pathspecs, in input order.</returns>
    public static List<string> ConfinePaths(string root, string[] paths)
        => paths is null ? [] : [.. paths.Select(path => PathConfinement.Confine(root, path))];

    /// <summary>
    /// Run <paramref name="body"/> and translate any <see cref="GitCheckException"/>
    /// (or unexpected exception) into a failure envelope. Success path returns
    /// the body's envelope verbatim.
    /// </summary>
    public async Task<ResultEnvelope> WrapAsync(CallContext ctx, RootHolder holder, Func<Task<ResultEnvelope>> body)
    {
        try
        {
            var envelope = await body().ConfigureAwait(false);
            // WrapAsync is the single owner of per-call outcome metrics, so a tool that runs
            // several git executions is still counted exactly once.
            metrics.RecordToolCall(ctx.Tool, "ok");
            return envelope;
        }
        catch (GitCheckException ex)
        {
            metrics.RecordToolCall(ctx.Tool, "error");
            ctx.Log(LogLevel.Error, "tool_error", message: ex.Message, extras: BuildErrorExtras(ex));
            return ResultEnvelope.Failure(ex, holder?.Value);
        }
        catch (Exception ex)
        {
            metrics.RecordToolCall(ctx.Tool, "error");
            var wrapped = new GitCommandException(
                "unexpected error inside tool. see server logs",
                new Dictionary<string, object> { ["detail"] = ex.GetType().Name });
            ctx.Log(LogLevel.Error, "tool_error", message: wrapped.Message, extras: BuildErrorExtras(wrapped));
            return ResultEnvelope.Failure(wrapped, holder?.Value);
        }
    }

    /// <summary>
    /// Build the <c>tool_error</c> log extras, always carrying the <c>error_code</c> and surfacing
    /// the git exit code when the failure originated from a non-zero subprocess exit.
    /// </summary>
    private static Dictionary<string, object> BuildErrorExtras(GitCheckException ex)
    {
        var extras = new Dictionary<string, object> { [LogFields.ErrorCode] = ex.Code };
        if (ex.Detail.TryGetValue(LogFields.GitExitCode, out var exitCode))
        {
            extras[LogFields.GitExitCode] = exitCode;
        }

        return extras;
    }

    /// <summary>Wrap a single-object payload as a one-element <see cref="ResultEnvelope.Results"/> list.</summary>
    public static ResultEnvelope SingleSuccess(
        object payload,
        string repoRoot,
        int? preFilterCount = null,
        IDictionary<string, object> filtersApplied = null,
        bool truncated = false)
    {
        var items = new List<object> { payload };
        return ResultEnvelope.Success(items, repoRoot, preFilterCount, filtersApplied, truncated);
    }

    /// <summary>Wrap a list payload as a many-element <see cref="ResultEnvelope.Results"/> list.</summary>
    public static ResultEnvelope ListSuccess(
        IEnumerable<object> items,
        string repoRoot,
        int? preFilterCount = null,
        IDictionary<string, object> filtersApplied = null,
        bool truncated = false)
    {
        var list = new List<object>(items);
        return ResultEnvelope.Success(list, repoRoot, preFilterCount, filtersApplied, truncated);
    }
}