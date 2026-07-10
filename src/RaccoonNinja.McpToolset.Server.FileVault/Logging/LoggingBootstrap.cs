using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace RaccoonNinja.McpToolset.Server.FileVault.Logging;

/// <summary>
/// Configures Serilog with the allowlist JSON formatter. The default sink is stderr (Rust parity;
/// safe when a client spawns several vault instances concurrently); a rotating file sink is
/// opt-in via <c>VAULT_MCP_LOG_FILE</c>. SDK and framework categories are capped at Information
/// so a debug level cannot mirror JSON-RPC frames (which carry vault content) into the log.
/// </summary>
public static class LoggingBootstrap
{
    /// <summary>Build a Serilog logger from the current environment.</summary>
    /// <returns>The configured root logger.</returns>
    public static Logger BuildLogger()
        => Build(EnvironmentSnapshot());

    /// <summary>Build a Serilog logger from the provided env map (tests inject here).</summary>
    /// <param name="env">The environment snapshot to read configuration from.</param>
    /// <returns>The configured root logger.</returns>
    public static Logger Build(IDictionary<string, string> env)
    {
        ArgumentNullException.ThrowIfNull(env);
        var configuredLevel = ParseLevel(
            Read(env, LoggingConstants.EnvLogLevel) ?? Read(env, LoggingConstants.EnvLogLevelFallback));

        var configuration = new LoggerConfiguration()
            .MinimumLevel.Is(configuredLevel)
            .MinimumLevel.Override("ModelContextProtocol", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("System", LogEventLevel.Information);

        var fallbackToStderr = false;
        var configuredFile = Read(env, LoggingConstants.EnvLogFile);
        if (!string.IsNullOrWhiteSpace(configuredFile))
        {
            try
            {
                ConfigureFile(configuration, configuredFile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                ConfigureStderr(configuration);
                fallbackToStderr = true;
            }
        }
        else
        {
            ConfigureStderr(configuration);
        }

        var logger = configuration.CreateLogger();
        if (fallbackToStderr)
        {
            logger.Warning("{Event}", "log_file_rejected");
        }

        return logger;
    }

    private static void ConfigureFile(LoggerConfiguration configuration, string path)
    {
        // The rolling file sink opens its file lazily on first emit and swallows failures into
        // SelfLog, which would silently discard every log line. Probing eagerly routes an
        // unwritable path through the stderr fallback (and its warning) instead.
        ProbeWritable(path);
        configuration.WriteTo.File(
            new AllowlistJsonFormatter(),
            path: path,
            rollingInterval: RollingInterval.Infinite,
            rollOnFileSizeLimit: true,
            fileSizeLimitBytes: LoggingConstants.RotationMaxBytes,
            retainedFileCountLimit: LoggingConstants.RotationBackupCount,
            shared: false,
            encoding: System.Text.Encoding.UTF8,
            flushToDiskInterval: TimeSpan.FromMilliseconds(500));
    }

    private static void ProbeWritable(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
    }

    private static void ConfigureStderr(LoggerConfiguration configuration)
    {
        configuration.WriteTo.Console(
            formatter: new AllowlistJsonFormatter(),
            standardErrorFromLevel: LogEventLevel.Verbose);
    }

    private static string Read(IDictionary<string, string> env, string key)
        => env.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static LogEventLevel ParseLevel(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return LogEventLevel.Information;
        }

        return raw.Trim().ToUpperInvariant() switch
        {
            "TRACE" or "VERBOSE" => LogEventLevel.Verbose,
            "DEBUG" => LogEventLevel.Debug,
            "INFO" or "INFORMATION" => LogEventLevel.Information,
            "WARN" or "WARNING" => LogEventLevel.Warning,
            "ERROR" => LogEventLevel.Error,
            "FATAL" or "CRITICAL" => LogEventLevel.Fatal,
            _ => LogEventLevel.Information,
        };
    }

    private static Dictionary<string, string> EnvironmentSnapshot()
    {
        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key)
            {
                snapshot[key] = entry.Value as string ?? string.Empty;
            }
        }

        return snapshot;
    }
}