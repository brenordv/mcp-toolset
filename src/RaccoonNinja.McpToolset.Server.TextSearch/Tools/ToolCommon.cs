using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RaccoonNinja.McpToolset.Server.TextSearch.Configuration;
using RaccoonNinja.McpToolset.Server.TextSearch.Envelope;
using RaccoonNinja.McpToolset.Server.TextSearch.Errors;
using RaccoonNinja.McpToolset.Server.TextSearch.Logging;
using RaccoonNinja.McpToolset.Server.TextSearch.Metrics;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tools;

/// <summary>
/// Shared per-tool helpers: per-call context, envelope wrapping, refusal recording, and result
/// construction. <see cref="WrapAsync"/> is the single owner of the per-call outcome metric and the
/// one place any escaping exception becomes a failure envelope, so a raw .NET exception message,
/// which can carry an absolute path, never reaches model context.
/// </summary>
public sealed class ToolCommon(SessionMetrics metrics, ILoggerFactory loggerFactory)
{
    /// <summary>Create a per-call correlation context for <paramref name="tool"/>.</summary>
    /// <param name="tool">The tool name.</param>
    /// <returns>The call context.</returns>
    public CallContext MakeContext(string tool)
        => new(tool, loggerFactory.CreateLogger(tool));

    /// <summary>Record a refusal, both as a counter and a per-call log line, carrying only the reason.</summary>
    /// <param name="ctx">The call context.</param>
    /// <param name="reason">The refusal reason (for example <c>denylisted</c>, <c>out_of_root</c>, <c>binary</c>).</param>
    public void Refusal(CallContext ctx, string reason)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        metrics.RecordRefusal(reason);
        ctx.Log(
            LogLevel.Warning,
            "refusal",
            extras: new Dictionary<string, object>(StringComparer.Ordinal) { [LogFields.RefusalReason] = reason });
    }

    /// <summary>Record a regex match-timeout hit as a first-class refusal signal.</summary>
    /// <param name="ctx">The call context.</param>
    public void RegexTimeout(CallContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        metrics.RecordRegexTimeout();
        ctx.Log(
            LogLevel.Warning,
            "regex_timeout",
            extras: new Dictionary<string, object>(StringComparer.Ordinal) { [LogFields.RefusalReason] = "regex_timeout" });
    }

    /// <summary>Record that a call searched the whole base root (no <c>cwd</c> scope), as a counter and a log line.</summary>
    /// <param name="ctx">The call context.</param>
    public void WholeBase(CallContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        metrics.RecordWholeBaseCall();
        ctx.Log(LogLevel.Debug, "whole_base");
    }

    /// <summary>Record that a call targeted a package root, as a counter and a path-free log line carrying only the name.</summary>
    /// <param name="ctx">The call context.</param>
    /// <param name="name">The package root's operator-chosen name (never a path or subpath).</param>
    public void PackageRoot(CallContext ctx, string name)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        metrics.RecordPackageRootCall();
        ctx.Log(
            LogLevel.Debug,
            "package_root",
            extras: new Dictionary<string, object>(StringComparer.Ordinal) { [LogFields.PackageRoot] = name });
    }

    /// <summary>Record which root a selector-driven call entered: whole base (blank <c>cwd</c>) or a package root.</summary>
    /// <param name="ctx">The call context.</param>
    /// <param name="cwd">The raw <c>cwd</c> argument (blank means the whole base).</param>
    /// <param name="scope">The resolved scope.</param>
    public void ScopeEntered(CallContext ctx, string cwd, CallScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (string.IsNullOrWhiteSpace(cwd))
        {
            WholeBase(ctx);
        }
        else if (scope.Kind == ScopeKind.Package)
        {
            PackageRoot(ctx, scope.PackageName);
        }
    }

    /// <summary>Record that a call re-included otherwise-ignored paths, as a counter and a log line.</summary>
    /// <param name="ctx">The call context.</param>
    public void IncludeIgnoredUsed(CallContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        metrics.RecordIncludeIgnored();
        ctx.Log(LogLevel.Debug, "include_ignored");
    }

    /// <summary>Record that an agent regex fell back from the non-backtracking to the backtracking engine.</summary>
    /// <param name="ctx">The call context.</param>
    public void RegexFallback(CallContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        metrics.RecordRegexFallback();
        ctx.Log(
            LogLevel.Debug,
            "regex_fallback",
            extras: new Dictionary<string, object>(StringComparer.Ordinal) { [LogFields.RegexFallback] = true });
    }

    /// <summary>
    /// Run <paramref name="body"/> and translate any escaping exception into a failure envelope. A
    /// <see cref="TextSearchException"/> maps to its code and its already-safe message; any other
    /// exception maps to a generic internal error carrying only the exception type name, never its
    /// message (which can hold an absolute path).
    /// </summary>
    /// <param name="ctx">The call context.</param>
    /// <param name="body">The tool body.</param>
    /// <returns>The tool's envelope, or a failure envelope.</returns>
    public async Task<ResultEnvelope> WrapAsync(CallContext ctx, Func<Task<ResultEnvelope>> body)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(body);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var envelope = await body().ConfigureAwait(false);
            metrics.RecordToolCall(ctx.Tool, "ok");
            metrics.RecordDurationMs((int)stopwatch.ElapsedMilliseconds);
            if (envelope.Truncated)
            {
                metrics.RecordTruncation();
            }

            return envelope;
        }
        catch (TextSearchException ex)
        {
            metrics.RecordToolCall(ctx.Tool, "error");
            metrics.RecordDurationMs((int)stopwatch.ElapsedMilliseconds);
            if (ex.RefusalReason is not null)
            {
                // A boundary refusal (for example a cwd escaping the base) is counted in refusals_total and
                // emits a refusal log line in addition to the generic tool_error.
                Refusal(ctx, ex.RefusalReason);
            }

            ctx.Log(
                LogLevel.Warning,
                "tool_error",
                message: ex.Message,
                extras: new Dictionary<string, object>(StringComparer.Ordinal) { [LogFields.ErrorCode] = ex.Code });
            return ResultEnvelope.Failure(ex);
        }
        catch (Exception ex)
        {
            metrics.RecordToolCall(ctx.Tool, "error");
            metrics.RecordDurationMs((int)stopwatch.ElapsedMilliseconds);
            var wrapped = new TextSearchException(
                ErrorCodes.InternalError,
                "unexpected error inside tool; see server logs",
                new Dictionary<string, object>(StringComparer.Ordinal) { ["type"] = ex.GetType().Name });

            // The client envelope stays generic and path-free (wrapped); the operator-facing log carries
            // the real exception type and message, so the fault is actually diagnosable.
            ctx.Log(
                LogLevel.Error,
                "tool_error",
                message: $"unexpected error: {ex.GetType().FullName}: {ex.Message}",
                extras: new Dictionary<string, object>(StringComparer.Ordinal) { [LogFields.ErrorCode] = wrapped.Code });
            return ResultEnvelope.Failure(wrapped);
        }
    }

    /// <summary>Wrap a single payload object as a one-element success envelope.</summary>
    /// <param name="payload">The payload.</param>
    /// <param name="filtersApplied">The safe-echo of the shaping arguments.</param>
    /// <returns>The success envelope.</returns>
    public static ResultEnvelope SingleSuccess(object payload, IDictionary<string, object> filtersApplied = null)
        => ResultEnvelope.Success([payload], filtersApplied: filtersApplied);

    /// <summary>Wrap a list of payload objects as a success envelope with pagination metadata.</summary>
    /// <param name="items">The result items.</param>
    /// <param name="truncated">Whether the result was capped.</param>
    /// <param name="cursor">The continuation token, or <c>null</c>.</param>
    /// <param name="preFilterCount">The count before filtering, if applicable.</param>
    /// <param name="filtersApplied">The safe-echo of the shaping arguments.</param>
    /// <param name="skippedSymlinks">The count of skipped symlinked entries, or <c>null</c> to omit.</param>
    /// <returns>The success envelope.</returns>
    public static ResultEnvelope ListSuccess(
        IEnumerable<object> items,
        bool truncated = false,
        string cursor = null,
        int? preFilterCount = null,
        IDictionary<string, object> filtersApplied = null,
        int? skippedSymlinks = null)
        => ResultEnvelope.Success([.. items], preFilterCount, filtersApplied, truncated, cursor, skippedSymlinks);
}