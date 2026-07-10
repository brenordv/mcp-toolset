namespace RaccoonNinja.McpToolset.Server.FileVault.Logging;

/// <summary>Environment variable names and rotation limits for the logging bootstrap.</summary>
public static class LoggingConstants
{
    /// <summary>Opt-in log file path (additive over the Rust server's env surface).</summary>
    public const string EnvLogFile = "VAULT_MCP_LOG_FILE";

    /// <summary>Primary log level variable (Rust parity).</summary>
    public const string EnvLogLevel = "VAULT_MCP_LOG";

    /// <summary>Fallback log level variable, honored for drop-in parity with the Rust server.</summary>
    public const string EnvLogLevelFallback = "RUST_LOG";

    public const long RotationMaxBytes = 10L * 1024 * 1024;
    public const int RotationBackupCount = 5;
}