using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Logging;

/// <summary>
/// Configures Serilog with the allowlist JSON formatter, a rolling file sink (default:
/// <c>mcp-text-edit.log</c> next to the executable), and a stderr fallback when the file cannot be
/// opened. Honors <c>MCP_TEXTEDIT_LOG_FILE</c> / <c>MCP_TEXTEDIT_LOG_LEVEL</c>. Never writes to
/// stdout, which the stdio transport owns.
/// </summary>
public static class LoggingBootstrap
{
    /// <summary>Build a Serilog logger from the current process environment.</summary>
    /// <returns>The configured logger.</returns>
    public static Logger BuildLogger()
        => Build(EnvironmentSnapshot());

    /// <summary>Build a Serilog logger from an explicit environment map (tests inject here).</summary>
    /// <param name="env">The environment variables to read configuration from.</param>
    /// <returns>The configured logger.</returns>
    public static Logger Build(IDictionary<string, string> env)
    {
        ArgumentNullException.ThrowIfNull(env);

        var configuration = new LoggerConfiguration()
            .MinimumLevel.Is(ParseLevel(Read(env, LoggingConstants.EnvLogLevel)));

        var configuredFile = Read(env, LoggingConstants.EnvLogFile);
        var target = string.IsNullOrWhiteSpace(configuredFile) ? DefaultLogPath() : configuredFile;

        var fellBack = false;
        try
        {
            ConfigureFile(configuration, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            ConfigureStderr(configuration);
            fellBack = true;
        }

        var logger = configuration.CreateLogger();
        if (fellBack)
        {
            logger.Warning("{Event}", "log_file_rejected");
        }

        return logger;
    }

    /// <summary>The default log path: <c>&lt;executable-dir&gt;/mcp-text-edit.log</c>.</summary>
    /// <returns>The absolute default log path.</returns>
    /// <remarks>
    /// Uses <see cref="AppContext.BaseDirectory"/> because it resolves correctly in single-file
    /// published apps, where <see cref="System.Reflection.Assembly.Location"/> is empty.
    /// </remarks>
    public static string DefaultLogPath()
        => Path.Combine(AppContext.BaseDirectory, LoggingConstants.DefaultLogFileName);

    private static void ConfigureFile(LoggerConfiguration configuration, string path)
    {
        configuration.WriteTo.File(
            new AllowlistJsonFormatter(),
            path: path,
            rollingInterval: RollingInterval.Infinite,
            rollOnFileSizeLimit: true,
            fileSizeLimitBytes: LoggingConstants.RotationMaxBytes,
            retainedFileCountLimit: LoggingConstants.RotationBackupCount,
            shared: false,
            encoding: new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            flushToDiskInterval: TimeSpan.FromMilliseconds(500));
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