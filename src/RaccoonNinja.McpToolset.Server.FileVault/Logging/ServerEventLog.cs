using Serilog;

namespace RaccoonNinja.McpToolset.Server.FileVault.Logging;

/// <summary>
/// Centralizes server-lifecycle log records so they honor the <see cref="LogFields"/> contract.
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
    {
        ArgumentNullException.ThrowIfNull(logger);
        return logger.ForContext(LogFields.Tool, ServerTool);
    }

    /// <summary>Emit the <c>server_start</c> record with the resolved configuration summary.</summary>
    /// <param name="serverLogger">The server-scoped logger from <see cref="ForServer"/>.</param>
    /// <param name="homePath">The store root path.</param>
    /// <param name="maxContentBytes">The per-call content limit.</param>
    /// <param name="busyTimeoutMs">The SQLite busy timeout.</param>
    /// <param name="splitHintChars">The split-hint threshold (0 when disabled).</param>
    public static void Start(ILogger serverLogger, string homePath, long maxContentBytes, int busyTimeoutMs, int splitHintChars)
    {
        ArgumentNullException.ThrowIfNull(serverLogger);
        serverLogger
            .ForContext(LogFields.Event, "server_start")
            .ForContext(LogFields.HomePath, homePath)
            .ForContext(LogFields.MaxContentBytes, maxContentBytes)
            .ForContext(LogFields.BusyTimeoutMs, busyTimeoutMs)
            .ForContext(LogFields.SplitHintChars, splitHintChars)
            .Information("server_start");
    }

    /// <summary>Emit the <c>store_open</c> record with the startup store summary.</summary>
    /// <param name="serverLogger">The server-scoped logger from <see cref="ForServer"/>.</param>
    /// <param name="schemaVersion">The database schema version.</param>
    /// <param name="activeFiles">The count of active files.</param>
    /// <param name="archivedFiles">The count of archived files.</param>
    /// <param name="versionRows">The total version-row count.</param>
    /// <param name="dbSizeBytes">The database file size in bytes.</param>
    /// <param name="migrationsApplied">The number of migrations applied on this startup.</param>
    public static void StoreOpen(
        ILogger serverLogger,
        long schemaVersion,
        long activeFiles,
        long archivedFiles,
        long versionRows,
        long dbSizeBytes,
        int migrationsApplied)
    {
        ArgumentNullException.ThrowIfNull(serverLogger);
        serverLogger
            .ForContext(LogFields.Event, "store_open")
            .ForContext(LogFields.SchemaVersion, schemaVersion)
            .ForContext(LogFields.ActiveFiles, activeFiles)
            .ForContext(LogFields.ArchivedFiles, archivedFiles)
            .ForContext(LogFields.VersionRows, versionRows)
            .ForContext(LogFields.DbSizeBytes, dbSizeBytes)
            .ForContext(LogFields.MigrationsApplied, migrationsApplied)
            .Information("store_open");
    }

    /// <summary>Emit the fatal <c>server_start_failed</c> record before a nonzero exit.</summary>
    /// <param name="serverLogger">The server-scoped logger from <see cref="ForServer"/>.</param>
    /// <param name="exception">The startup failure.</param>
    public static void StartFailed(ILogger serverLogger, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(serverLogger);
        serverLogger.ForContext(LogFields.Event, "server_start_failed").Fatal(exception, "server_start_failed");
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
    /// <param name="reason">A short human-readable reason for the shutdown.</param>
    public static void Stop(ILogger serverLogger, IDictionary<string, object> metricsSummary, string reason)
    {
        ArgumentNullException.ThrowIfNull(serverLogger);
        serverLogger
            .ForContext(LogFields.Event, "server_stop")
            .ForContext(LogFields.MetricsSummary, metricsSummary, destructureObjects: true)
            .Information("server stop ({Reason})", reason);
    }

    /// <summary>Warn that the store looks like it lives on a network or synced volume.</summary>
    /// <param name="serverLogger">The server-scoped logger from <see cref="ForServer"/>.</param>
    /// <param name="homePath">The suspicious store root path.</param>
    public static void NonLocalStoreWarning(ILogger serverLogger, string homePath)
    {
        ArgumentNullException.ThrowIfNull(serverLogger);
        serverLogger
            .ForContext(LogFields.Event, "store_non_local")
            .ForContext(LogFields.HomePath, homePath)
            .Warning(
                "the vault store appears to be on a network or synced drive; SQLite's cross-process "
                + "locking is unreliable there — move VAULT_MCP_HOME to a local disk if you see corruption");
    }
}