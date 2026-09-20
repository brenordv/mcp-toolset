using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using RaccoonNinja.McpToolset.Server.FileVault.Errors;
using RaccoonNinja.McpToolset.Server.FileVault.Logging;
using RaccoonNinja.McpToolset.Server.FileVault.Metrics;
using RaccoonNinja.McpToolset.Server.FileVault.Services;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tools;

/// <summary>
/// Shared per-tool plumbing: the outcome/duration/metrics wrapper and the domain-error
/// translation. Domain failures become <see cref="McpException"/>s whose message is
/// the structured JSON error body (the SDK surfaces it as a failed tool result); everything else
/// is logged in full and surfaced generically.
/// </summary>
public sealed class ToolCommon(SessionMetrics metrics, ILoggerFactory loggerFactory)
{
    private const int SlowCallWarningMs = 1_000;
    private static long _callCounter;

    /// <summary>
    /// Run <paramref name="body"/> under the standard per-call contract: one structured log
    /// record per call (tool, project, name, duration, outcome), one metrics sample, and the
    /// domain-error translation. A <see cref="VaultException"/> is never logged as an exception
    /// object; only its code is recorded.
    /// </summary>
    /// <typeparam name="T">The tool's typed result.</typeparam>
    /// <param name="tool">The tool name.</param>
    /// <param name="body">The tool body; it receives a <see cref="CallInfo"/> to fill in identifiers.</param>
    /// <returns>The body's result.</returns>
    /// <exception cref="McpException">Thrown for both domain and internal failures, carrying the JSON error body.</exception>
    public T Run<T>(string tool, Func<CallInfo, T> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var logger = loggerFactory.CreateLogger(tool);
        var callId = Interlocked.Increment(ref _callCounter);
        var info = new CallInfo();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = body(info);
            stopwatch.Stop();
            metrics.RecordToolCall(tool, "ok");
            RecordDuration(tool, logger, stopwatch, callId);
            LogCall(logger, LogLevel.Debug, tool, callId, info, stopwatch.ElapsedMilliseconds, "ok", errorCode: null, exceptionType: null);
            return result;
        }
        catch (VaultException ex)
        {
            stopwatch.Stop();
            var code = ex.Code.ToWireCode();
            metrics.RecordToolCall(tool, code);
            RecordDuration(tool, logger, stopwatch, callId);
            LogCall(logger, LogLevel.Information, tool, callId, info, stopwatch.ElapsedMilliseconds, "domain_error", code, exceptionType: null, ex.BaseVersion, ex.CurrentVersion, ex.Reason);
            throw new McpException(ErrorMapping.ToErrorJson(ex));
        }
        catch (McpException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            metrics.RecordToolCall(tool, "internal_error");
            metrics.RecordInternalError();
            RecordDuration(tool, logger, stopwatch, callId);
            LogCall(logger, LogLevel.Error, tool, callId, info, stopwatch.ElapsedMilliseconds, "internal_error", errorCode: null, ex.GetType().Name);
            LogInternalException(logger, tool, callId, ex);
            throw new McpException(ErrorMapping.ToInternalErrorJson(ex));
        }
    }

    private void RecordDuration(string tool, ILogger logger, Stopwatch stopwatch, long callId)
    {
        var elapsed = (int)Math.Min(stopwatch.ElapsedMilliseconds, int.MaxValue);
        metrics.RecordDurationMs(elapsed);

        if (elapsed <= SlowCallWarningMs)
            return;

        // Scoped so the warning carries allowlisted fields and joins its tool_call record.
        var scope = new Dictionary<string, object>
        {
            [LogFields.Event] = "slow_call",
            [LogFields.Tool] = tool,
            [LogFields.CallId] = callId,
            [LogFields.DurationMs] = elapsed,
        };
        using (logger.BeginScope(scope))
        {
            LogSlowCall(logger, tool, elapsed);
        }
    }

    private static void LogCall(
        ILogger logger,
        LogLevel level,
        string tool,
        long callId,
        CallInfo info,
        long durationMs,
        string outcome,
        string errorCode,
        string exceptionType,
        int? baseVersion = null,
        int? currentVersion = null,
        string reason = null)
    {
        var scope = new Dictionary<string, object>
        {
            [LogFields.Event] = "tool_call",
            [LogFields.Tool] = tool,
            [LogFields.CallId] = callId,
            [LogFields.DurationMs] = durationMs,
        };
        if (info.Project is not null)
        {
            scope[LogFields.Project] = info.Project;
        }

        if (info.Name is not null)
        {
            scope[LogFields.Name] = info.Name;
        }

        if (info.ContentSizeBytes is { } size)
        {
            scope[LogFields.ContentSizeBytes] = size;
        }

        if (info.CommittedChars is { } committedChars)
        {
            scope[LogFields.CommittedChars] = committedChars;
        }

        if (info.QueryMode is not null)
        {
            scope[LogFields.QueryMode] = info.QueryMode;
        }

        if (info.PageItems is { } pageItems)
        {
            scope[LogFields.PageItems] = pageItems;
        }

        if (info.Truncated is { } truncated)
        {
            scope[LogFields.Truncated] = truncated;
        }

        if (info.CursorHash is not null)
        {
            scope[LogFields.CursorHash] = info.CursorHash;
        }

        if (info.NotesScanned is { } notesScanned)
        {
            scope[LogFields.NotesScanned] = notesScanned;
        }

        if (info.NotesMatched is { } notesMatched)
        {
            scope[LogFields.NotesMatched] = notesMatched;
        }

        if (info.BytesScanned is { } bytesScanned)
        {
            scope[LogFields.BytesScanned] = bytesScanned;
        }

        if (info.SkippedUnreadable is { } skippedUnreadable)
        {
            scope[LogFields.SkippedUnreadable] = skippedUnreadable;
        }

        if (errorCode is not null)
        {
            scope[LogFields.ErrorCode] = errorCode;
        }

        if (exceptionType is not null)
        {
            scope[LogFields.ExceptionType] = exceptionType;
        }

        if (baseVersion is { } bv)
        {
            scope[LogFields.BaseVersion] = bv;
        }

        if (currentVersion is { } cv)
        {
            scope[LogFields.CurrentVersion] = cv;
        }

        if (reason is not null)
        {
            scope[LogFields.Reason] = reason;
        }

        using (logger.BeginScope(scope))
        {
            LogOutcome(logger, level, outcome);
        }
    }

    private static void LogOutcome(ILogger logger, LogLevel level, string outcome)
    {
        switch (level)
        {
            case LogLevel.Error:
                LogOutcomeError(logger, outcome, null);
                break;
            case LogLevel.Information:
                LogOutcomeInformation(logger, outcome, null);
                break;
            case LogLevel.Trace or LogLevel.Debug or LogLevel.Warning or LogLevel.Critical or LogLevel.None:
            default:
                LogOutcomeDebug(logger, outcome, null);
                break;
        }
    }

    private static readonly Action<ILogger, string, Exception> LogOutcomeDebug =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(2100, "tool_call"), "tool_call {Outcome}");

    private static readonly Action<ILogger, string, Exception> LogOutcomeInformation =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(2101, "tool_call"), "tool_call {Outcome}");

    private static readonly Action<ILogger, string, Exception> LogOutcomeError =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(2102, "tool_call"), "tool_call {Outcome}");

    private static readonly Action<ILogger, string, int, Exception> LogSlowCallMessage =
        LoggerMessage.Define<string, int>(LogLevel.Warning, new EventId(2103, "slow_call"), "slow tool call: {Tool} took {DurationMs} ms");

    private static readonly Action<ILogger, Exception> LogInternalExceptionMessage =
        LoggerMessage.Define(LogLevel.Error, new EventId(2104, "internal_error"), "internal error inside tool");

    private static void LogSlowCall(ILogger logger, string tool, int durationMs)
        => LogSlowCallMessage(logger, tool, durationMs, null);

    /// <summary>Non-domain exceptions are logged in full: SQLite/IO text carries paths and error codes, not vault content.</summary>
    private static void LogInternalException(ILogger logger, string tool, long callId, Exception exception)
    {
        var scope = new Dictionary<string, object>
        {
            [LogFields.Event] = "internal_error",
            [LogFields.Tool] = tool,
            [LogFields.CallId] = callId,
        };
        using (logger.BeginScope(scope))
        {
            LogInternalExceptionMessage(logger, exception);
        }
    }

    private const int MaxSnapshotWarningRecords = 5;

    /// <summary>
    /// Content-free query debuggability shared by the query tools: token count, byte length, and a
    /// hash of the query, never the query text itself. A null query logs nothing.
    /// </summary>
    /// <param name="tool">The calling tool name.</param>
    /// <param name="query">The raw query text, or null.</param>
    public void LogQueryShape(string tool, string query)
    {
        if (query is null)
        {
            return;
        }

        var logger = loggerFactory.CreateLogger(tool);
        var tokens = query.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length;
        var bytes = Encoding.UTF8.GetByteCount(query);
        var hash = LogScrubbing.HashedParameter(query);
        var scope = new Dictionary<string, object>
        {
            [LogFields.Event] = "query_shape",
            [LogFields.Tool] = tool,
            [LogFields.QueryTokens] = tokens,
            [LogFields.QueryBytes] = bytes,
            [LogFields.QueryHash] = hash,
        };
        using (logger.BeginScope(scope))
        {
            LogQueryShapeMessage(logger, tokens, bytes, hash, null);
        }
    }

    /// <summary>
    /// Record and log that a query fell back to the ranked any-term pass. This is the transition the
    /// feature exists for, so it rides an Information-level event at the shipped default log level.
    /// </summary>
    /// <param name="tool">The calling tool name.</param>
    /// <param name="query">The raw query text (only its shape is logged).</param>
    /// <param name="matched">How many notes the fallback matched.</param>
    public void LogQueryFallback(string tool, string query, int matched)
    {
        metrics.RecordQueryFallback();
        var logger = loggerFactory.CreateLogger(tool);
        var tokens = query?.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length ?? 0;
        var hash = LogScrubbing.HashedParameter(query);
        var scope = new Dictionary<string, object>
        {
            [LogFields.Event] = "query_fallback",
            [LogFields.Tool] = tool,
            [LogFields.QueryTokens] = tokens,
            [LogFields.QueryHash] = hash,
            [LogFields.NotesMatched] = matched,
        };
        using (logger.BeginScope(scope))
        {
            LogQueryFallbackMessage(logger, tool, matched, null);
        }
    }

    /// <summary>
    /// Record and log snapshots <c>vault_search</c> could not read: a Warning summary plus per-note
    /// records (project, name, exception type only) capped at <see cref="MaxSnapshotWarningRecords"/>.
    /// Store damage must be visible at the default log level.
    /// </summary>
    /// <param name="tool">The calling tool name.</param>
    /// <param name="skips">The skipped notes; a null or empty list is a no-op.</param>
    public void ReportSnapshotSkips(string tool, IReadOnlyList<SnapshotSkip> skips)
    {
        if (skips is null || skips.Count == 0)
        {
            return;
        }

        metrics.RecordSnapshotReadFailures(skips.Count);
        var logger = loggerFactory.CreateLogger(tool);
        var summaryScope = new Dictionary<string, object>
        {
            [LogFields.Event] = "snapshot_unreadable",
            [LogFields.Tool] = tool,
            [LogFields.SkippedUnreadable] = skips.Count,
        };
        using (logger.BeginScope(summaryScope))
        {
            LogSnapshotUnreadableSummary(logger, tool, skips.Count, null);
        }

        foreach (var skip in skips.Take(MaxSnapshotWarningRecords))
        {
            var scope = new Dictionary<string, object>
            {
                [LogFields.Event] = "snapshot_unreadable",
                [LogFields.Tool] = tool,
                [LogFields.Project] = skip.Project,
                [LogFields.Name] = skip.Name,
                [LogFields.ExceptionType] = skip.ExceptionType,
            };
            using (logger.BeginScope(scope))
            {
                LogSnapshotUnreadableNote(logger, skip.Project, skip.Name, null);
            }
        }
    }

    private static readonly Action<ILogger, int, int, string, Exception> LogQueryShapeMessage =
        LoggerMessage.Define<int, int, string>(
            LogLevel.Debug,
            new EventId(2110, "query_shape"),
            "query shape: tokens={QueryTokens} bytes={QueryBytes} hash={QueryHash}");

    private static readonly Action<ILogger, string, int, Exception> LogQueryFallbackMessage =
        LoggerMessage.Define<string, int>(
            LogLevel.Information,
            new EventId(2111, "query_fallback"),
            "query fell back to any-term matching: {Tool} matched {NotesMatched}");

    private static readonly Action<ILogger, string, int, Exception> LogSnapshotUnreadableSummary =
        LoggerMessage.Define<string, int>(
            LogLevel.Warning,
            new EventId(2112, "snapshot_unreadable"),
            "{Tool} skipped {SkippedUnreadable} unreadable snapshot(s)");

    private static readonly Action<ILogger, string, string, Exception> LogSnapshotUnreadableNote =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(2113, "snapshot_unreadable"),
            "unreadable snapshot: {Project}/{Name}");
}