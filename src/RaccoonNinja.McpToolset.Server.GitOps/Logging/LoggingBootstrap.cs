using System.Reflection;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.LogFileValidatorExceptions;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace RaccoonNinja.McpToolset.Server.GitOps.Logging;

/// <summary>
/// Configures Serilog with the allowlist JSON formatter, a rotating file sink
/// (default: <c>mcp-gitops.log</c> next to the executable), and a stderr fallback.
/// Honors <c>MCP_GITOPS_LOG_FILE</c> / <c>MCP_GITOPS_LOG_LEVEL</c> for configuration.
/// </summary>
public static class LoggingBootstrap
{
    /// <summary>Build a Serilog logger from the current environment.</summary>
    public static Logger BuildLogger()
        => Build(EnvironmentSnapshot());

    /// <summary>Build a Serilog logger from the provided env map (tests inject here).</summary>
    public static Logger Build(IDictionary<string, string> env)
    {
        var configuredLevel = ParseLevel(Read(env, LoggingConstants.EnvLogLevel));
        var configuredFile = Read(env, LoggingConstants.EnvLogFile);

        var configuration = new LoggerConfiguration()
            .MinimumLevel.Is(configuredLevel);

        var fallbackToStderr = false;

        if (!string.IsNullOrWhiteSpace(configuredFile))
        {
            try
            {
                var target = LogFileValidator.Validate(configuredFile);
                ConfigureFile(configuration, target);
            }
            catch (LogPathRejectedException)
            {
                ConfigureStderr(configuration);
                fallbackToStderr = true;
            }
        }
        else
        {
            var defaultPath = DefaultLogPath();
            try
            {
                ConfigureFile(configuration, defaultPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ConfigureStderr(configuration);
                fallbackToStderr = true;
            }
        }

        var logger = configuration.CreateLogger();
        if (fallbackToStderr)
        {
            logger.Warning("{Event}", "log_file_rejected");
            logger.Debug("{Event}", "log_file_rejected_detail");
        }

        return logger;
    }

    /// <summary>Compute the default log path: <c>&lt;executable-dir&gt;/mcp-gitops.log</c>.</summary>
    public static string DefaultLogPath()
    {
        var assemblyDir = AppContext.BaseDirectory;
        if (string.IsNullOrEmpty(assemblyDir))
        {
            assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        }
        return Path.Combine(assemblyDir ?? string.Empty, "mcp-gitops.log");
    }

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
            encoding: System.Text.Encoding.UTF8,
            flushToDiskInterval: TimeSpan.FromMilliseconds(500));
    }

    private static void ConfigureStderr(LoggerConfiguration configuration)
    {
        configuration.WriteTo.Console(
            formatter: new AllowlistJsonFormatter(),
            standardErrorFromLevel: LogEventLevel.Verbose);
    }

    private static string Read(IDictionary<string, string> env, string key)
    {
        if (env.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;

        return null;
    }

    private static LogEventLevel ParseLevel(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return LogEventLevel.Information;

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