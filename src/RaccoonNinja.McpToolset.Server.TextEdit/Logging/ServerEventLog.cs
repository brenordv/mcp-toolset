using Serilog;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Logging;

/// <summary>
/// Server-lifecycle log records, honoring the <see cref="LogFields"/> contract (notably the lowercase
/// <c>event</c> field). Runs outside dependency injection, before and after the host, so it operates on
/// a Serilog <see cref="ILogger"/> directly.
/// </summary>
public static class ServerEventLog
{
    private const string ServerTool = "server";

    /// <summary>Create the server-scoped logger every lifecycle record derives from.</summary>
    /// <param name="logger">The root Serilog logger.</param>
    /// <returns>A logger with <c>tool=server</c> attached.</returns>
    public static ILogger ForServer(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        return logger.ForContext(LogFields.Tool, ServerTool);
    }

    /// <summary>Emit the operator-facing startup scope line: the root hash and the effective caps.</summary>
    /// <param name="serverLogger">The server-scoped logger from <see cref="ForServer"/>.</param>
    /// <param name="rootHash">The 8-char hash of the canonical root (never the absolute path).</param>
    /// <param name="caps">A short, path-free summary of the effective caps.</param>
    public static void Scope(ILogger serverLogger, string rootHash, string caps)
    {
        ArgumentNullException.ThrowIfNull(serverLogger);
        serverLogger
            .ForContext(LogFields.Event, "server_scope")
            .ForContext(LogFields.RootHash, rootHash)
            .Information("server scope: {Caps}", caps);
    }

    /// <summary>Emit the <c>server_start_failed</c> record for a fatal configuration or startup error.</summary>
    /// <param name="serverLogger">The server-scoped logger from <see cref="ForServer"/>.</param>
    /// <param name="exception">The exception that aborted startup.</param>
    public static void StartFailed(ILogger serverLogger, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(serverLogger);
        serverLogger.ForContext(LogFields.Event, "server_start_failed").Error(exception, "server_start_failed");
    }

    /// <summary>Emit the <c>server_stop_failed</c> record carrying the fatal exception.</summary>
    /// <param name="serverLogger">The server-scoped logger from <see cref="ForServer"/>.</param>
    /// <param name="exception">The exception that aborted the host.</param>
    public static void StopFailed(ILogger serverLogger, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(serverLogger);
        serverLogger.ForContext(LogFields.Event, "server_stop_failed").Error(exception, "server_stop_failed");
    }

    /// <summary>Emit the <c>server_stop</c> record with the session metrics summary.</summary>
    /// <param name="serverLogger">The server-scoped logger from <see cref="ForServer"/>.</param>
    /// <param name="metricsSummary">The shutdown metrics snapshot to attach.</param>
    /// <param name="reason">A short, human-readable shutdown reason.</param>
    public static void Stop(ILogger serverLogger, IDictionary<string, object> metricsSummary, string reason)
    {
        ArgumentNullException.ThrowIfNull(serverLogger);
        serverLogger
            .ForContext(LogFields.Event, "server_stop")
            .ForContext(LogFields.MetricsSummary, metricsSummary, destructureObjects: true)
            .Information("server stop ({Reason})", reason);
    }
}