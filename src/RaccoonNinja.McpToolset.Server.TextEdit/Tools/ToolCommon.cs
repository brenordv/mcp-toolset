using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RaccoonNinja.McpToolset.Server.TextEdit.Envelope;
using RaccoonNinja.McpToolset.Server.TextEdit.Errors;
using RaccoonNinja.McpToolset.Server.TextEdit.Logging;
using RaccoonNinja.McpToolset.Server.TextEdit.Metrics;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Tools;

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
    /// <param name="reason">The refusal reason (for example <c>denylisted</c>, <c>out_of_root</c>, <c>ignored</c>).</param>
    public void Refusal(CallContext ctx, string reason)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        metrics.RecordRefusal(reason);
        ctx.Log(
            LogLevel.Warning,
            "refusal",
            extras: new Dictionary<string, object>(StringComparer.Ordinal) { [LogFields.RefusalReason] = reason });
    }

    /// <summary>Record that a call edited the whole base root (no <c>cwd</c> scope, the widest write), as a counter and a log line.</summary>
    /// <param name="ctx">The call context.</param>
    public void WholeBase(CallContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        metrics.RecordWholeBaseCall();
        ctx.Log(LogLevel.Debug, "whole_base");
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

    /// <summary>Record a completed mutation batch: its counts, whether it was a dry run, and its journal id.</summary>
    /// <param name="ctx">The call context.</param>
    /// <param name="batchId">The committed batch id, or <c>null</c> for a dry run or a no-op batch.</param>
    /// <param name="attempted">How many files the batch attempted.</param>
    /// <param name="changed">How many files the batch changed.</param>
    /// <param name="refused">How many files the batch refused.</param>
    /// <param name="dryRun">Whether the batch was a dry run.</param>
    public void BatchCommitted(CallContext ctx, long? batchId, int attempted, int changed, int refused, bool dryRun)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (!dryRun)
        {
            metrics.RecordBatchCommitted(changed);
        }

        var extras = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [LogFields.Attempted] = attempted,
            [LogFields.Changed] = changed,
            [LogFields.Refused] = refused,
            [LogFields.DryRun] = dryRun,
        };
        if (batchId.HasValue)
        {
            extras[LogFields.BatchId] = batchId.Value;
        }

        ctx.Log(LogLevel.Information, "batch", extras: extras);
    }

    /// <summary>Record an undo: how many files it restored, for the given batch.</summary>
    /// <param name="ctx">The call context.</param>
    /// <param name="batchId">The batch that was undone.</param>
    /// <param name="restored">How many files were restored.</param>
    public void Restored(CallContext ctx, long batchId, int restored)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        metrics.RecordFilesRestored(restored);
        ctx.Log(
            LogLevel.Information,
            "undo",
            extras: new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [LogFields.BatchId] = batchId,
                [LogFields.Changed] = restored,
            });
    }

    /// <summary>
    /// Run <paramref name="body"/> and translate any escaping exception into a failure envelope. A
    /// <see cref="TextEditException"/> maps to its code and its already-safe message; any other
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
        catch (TextEditException ex)
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
            var wrapped = new TextEditException(
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
    /// <param name="filtersApplied">The safe-echo of the shaping arguments.</param>
    /// <param name="skippedSymlinks">The count of skipped symlinked entries, or <c>null</c> to omit.</param>
    /// <returns>The success envelope.</returns>
    public static ResultEnvelope ListSuccess(
        IEnumerable<object> items,
        bool truncated = false,
        IDictionary<string, object> filtersApplied = null,
        int? skippedSymlinks = null)
        => ResultEnvelope.Success([.. items], filtersApplied, truncated, skippedSymlinks);
}