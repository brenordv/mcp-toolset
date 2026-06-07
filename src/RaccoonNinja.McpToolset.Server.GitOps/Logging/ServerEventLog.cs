using Serilog;

namespace RaccoonNinja.McpToolset.Server.GitOps.Logging;

/// <summary>
/// Centralizes server-lifecycle log records so they honor the <see cref="LogFields"/> contract;
/// notably the lowercase <c>event</c> field. Consistently with the per-tool <c>CallContext</c> path.
/// Runs outside dependency injection (before and after the host), so it operates on a Serilog
/// <see cref="ILogger"/> directly.
/// </summary>
public static class ServerEventLog
{
    private const string ServerTool = "server";

    /// <summary>Create the server-scoped logger every lifecycle record derives from.</summary>
    /// <param name="logger">The root Serilog logger.</param>
    /// <returns>A logger with the <c>tool=server</c> context attached.</returns>
    public static ILogger ForServer(ILogger logger)
        => logger.ForContext(LogFields.Tool, ServerTool);

    /// <summary>Emit the <c>server_start</c> record.</summary>
    /// <param name="serverLogger">The server-scoped logger from <see cref="ForServer"/>.</param>
    public static void Start(ILogger serverLogger)
        => serverLogger.ForContext(LogFields.Event, "server_start").Information("server_start");

    /// <summary>Emit the <c>server_stop_failed</c> record carrying the fatal exception.</summary>
    /// <param name="serverLogger">The server-scoped logger from <see cref="ForServer"/>.</param>
    /// <param name="exception">The exception that aborted the host.</param>
    public static void StopFailed(ILogger serverLogger, Exception exception)
        => serverLogger.ForContext(LogFields.Event, "server_stop_failed").Error(exception, "server_stop_failed");

    /// <summary>Emit the <c>server_stop</c> record with the session metrics summary.</summary>
    /// <param name="serverLogger">The server-scoped logger from <see cref="ForServer"/>.</param>
    /// <param name="metricsSummary">The shutdown metrics snapshot to attach.</param>
    /// <param name="reason">A short human-readable reason for the shutdown.</param>
    public static void Stop(ILogger serverLogger, IDictionary<string, object> metricsSummary, string reason)
        => serverLogger
            .ForContext(LogFields.Event, "server_stop")
            .ForContext(LogFields.MetricsSummary, metricsSummary, destructureObjects: true)
            .Information("server stop ({Reason})", reason);
}