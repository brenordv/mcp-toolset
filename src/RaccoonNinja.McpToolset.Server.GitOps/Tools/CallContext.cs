using Microsoft.Extensions.Logging;
using RaccoonNinja.McpToolset.Server.GitOps.Logging;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tools;

/// <summary>
/// Per-tool-call correlation handle: tool name + monotonic call id. Emits log
/// records with both attached so the 1:1 correlation invariant holds.
/// </summary>
public sealed partial class CallContext(string tool, ILogger logger)
{
    private static long _counter;

    public string Tool { get; } = tool;
    public int CallId { get; } = (int)Interlocked.Increment(ref _counter);
    private ILogger Logger { get; } = logger;

    /// <summary>Emit a structured log record bound to this call.</summary>
    /// <param name="level">The record's level.</param>
    /// <param name="event">The event name, written as the <c>event</c> field.</param>
    /// <param name="message">The record's message; the event name when <c>null</c>.</param>
    /// <param name="extras">More allowlisted fields for the record, or <c>null</c>.</param>
    /// <param name="exception">
    /// The exception the record is about, or <c>null</c>. The log formatter writes only its type chain, never its message
    /// or stack trace.
    /// </param>
    public void Log(LogLevel level, string @event, string message = null, IDictionary<string, object> extras = null, Exception exception = null)
    {
        var scope = new Dictionary<string, object>
        {
            [LogFields.Event] = @event,
            [LogFields.Tool] = Tool,
            [LogFields.CallId] = CallId,
        };
        if (extras != null)
        {
            foreach (var kvp in extras)
            {
                scope[kvp.Key] = kvp.Value;
            }
        }
        var text = message ?? @event;
        using (Logger.BeginScope(scope))
        {
            switch (level)
            {
                case LogLevel.Trace: EventTrace(Logger, exception, text); break;
                case LogLevel.Debug: EventDebug(Logger, exception, text); break;
                case LogLevel.Information: EventInformation(Logger, exception, text); break;
                case LogLevel.Warning: EventWarning(Logger, exception, text); break;
                case LogLevel.Error: EventError(Logger, exception, text); break;
                case LogLevel.Critical: EventCritical(Logger, exception, text); break;
                case LogLevel.None: break;
            }
        }
    }

    // Source-generated LoggerMessage delegates (CA1848). Parameter name "Event"
    // must match the template placeholder; this is the framework contract for
    // LoggerMessage source generation.

    [LoggerMessage(EventId = 1100, Level = LogLevel.Trace, Message = "{eventMessage}")]
    private static partial void EventTrace(ILogger logger, Exception exception, string eventMessage);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Debug, Message = "{eventMessage}")]
    private static partial void EventDebug(ILogger logger, Exception exception, string eventMessage);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Information, Message = "{eventMessage}")]
    private static partial void EventInformation(ILogger logger, Exception exception, string eventMessage);

    [LoggerMessage(EventId = 1103, Level = LogLevel.Warning, Message = "{eventMessage}")]
    private static partial void EventWarning(ILogger logger, Exception exception, string eventMessage);

    [LoggerMessage(EventId = 1104, Level = LogLevel.Error, Message = "{eventMessage}")]
    private static partial void EventError(ILogger logger, Exception exception, string eventMessage);

    [LoggerMessage(EventId = 1105, Level = LogLevel.Critical, Message = "{eventMessage}")]
    private static partial void EventCritical(ILogger logger, Exception exception, string eventMessage);
}