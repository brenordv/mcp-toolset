namespace RaccoonNinja.McpToolset.Server.GitOps.Logging;

public static class LoggingConstants
{
    public const string EnvLogFile = "MCP_GITOPS_LOG_FILE";
    public const string EnvLogLevel = "MCP_GITOPS_LOG_LEVEL";

    public const long RotationMaxBytes = 10L * 1024 * 1024;
    public const int RotationBackupCount = 5;
}