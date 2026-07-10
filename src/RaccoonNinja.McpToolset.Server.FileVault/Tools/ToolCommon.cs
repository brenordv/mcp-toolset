using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using RaccoonNinja.McpToolset.Server.FileVault.Errors;
using RaccoonNinja.McpToolset.Server.FileVault.Logging;
using RaccoonNinja.McpToolset.Server.FileVault.Metrics;

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
    /// object — only its code is recorded.
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
            LogCall(logger, LogLevel.Information, tool, callId, info, stopwatch.ElapsedMilliseconds, "domain_error", code, exceptionType: null, ex.BaseVersion, ex.CurrentVersion);
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
        int? currentVersion = null)
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
}