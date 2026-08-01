namespace RaccoonNinja.McpToolset.Server.TextEdit.Logging;

/// <summary>Environment keys and rotation limits for the log sink.</summary>
public static class LoggingConstants
{
    /// <summary>Optional absolute path for the rolling log file; unset uses the default next to the executable.</summary>
    public const string EnvLogFile = "MCP_TEXTEDIT_LOG_FILE";

    /// <summary>Optional minimum log level (TRACE/DEBUG/INFO/WARN/ERROR/FATAL); unset means INFO.</summary>
    public const string EnvLogLevel = "MCP_TEXTEDIT_LOG_LEVEL";

    /// <summary>The default log file name, written next to the executable.</summary>
    public const string DefaultLogFileName = "mcp-text-edit.log";

    /// <summary>The size at which the log file rolls, in bytes.</summary>
    public const long RotationMaxBytes = 10L * 1024 * 1024;

    /// <summary>How many rolled log files to retain.</summary>
    public const int RotationBackupCount = 5;
}