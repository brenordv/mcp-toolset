using Microsoft.Extensions.Logging;
using RaccoonNinja.McpToolset.Server.TextEdit.Logging;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Tools;

/// <summary>
/// Per-tool-call correlation handle: tool name plus a monotonic call id. Emits log records with both
/// attached so a refusal, a metric, and a result from one call are a single filter, not a
/// reconstruction.
/// </summary>
public sealed partial class CallContext(string tool, ILogger logger)
{
    private static long _counter;

    /// <summary>The tool this call belongs to.</summary>
    public string Tool { get; } = tool;

    /// <summary>The monotonic per-process call id.</summary>
    public int CallId { get; } = (int)Interlocked.Increment(ref _counter);

    private ILogger Logger { get; } = logger;

    /// <summary>Emit a structured log record bound to this call.</summary>
    /// <param name="level">The log level.</param>
    /// <param name="event">The event name for the <c>event</c> field.</param>
    /// <param name="message">The free-text message; defaults to the event name.</param>
    /// <param name="extras">Additional allowlisted fields to attach.</param>
    public void Log(LogLevel level, string @event, string message = null, IDictionary<string, object> extras = null)
    {
        var scope = new Dictionary<string, object>(StringComparer.Ordinal)
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
                case LogLevel.Trace: EventTrace(Logger, text); break;
                case LogLevel.Debug: EventDebug(Logger, text); break;
                case LogLevel.Information: EventInformation(Logger, text); break;
                case LogLevel.Warning: EventWarning(Logger, text); break;
                case LogLevel.Error: EventError(Logger, text); break;
                case LogLevel.Critical: EventCritical(Logger, text); break;
                case LogLevel.None: break;
                default: break;
            }
        }
    }

    // Source-generated LoggerMessage delegates (CA1848).

    [LoggerMessage(EventId = 1100, Level = LogLevel.Trace, Message = "{eventMessage}")]
    private static partial void EventTrace(ILogger logger, string eventMessage);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Debug, Message = "{eventMessage}")]
    private static partial void EventDebug(ILogger logger, string eventMessage);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Information, Message = "{eventMessage}")]
    private static partial void EventInformation(ILogger logger, string eventMessage);

    [LoggerMessage(EventId = 1103, Level = LogLevel.Warning, Message = "{eventMessage}")]
    private static partial void EventWarning(ILogger logger, string eventMessage);

    [LoggerMessage(EventId = 1104, Level = LogLevel.Error, Message = "{eventMessage}")]
    private static partial void EventError(ILogger logger, string eventMessage);

    [LoggerMessage(EventId = 1105, Level = LogLevel.Critical, Message = "{eventMessage}")]
    private static partial void EventCritical(ILogger logger, string eventMessage);
}