namespace RaccoonNinja.McpToolset.Server.GitOps.Logging;

/// <summary>
/// The fixed field allowlist defined by the logging contract. Any other key is
/// dropped at format time so a future bug cannot silently leak a redacted value.
/// </summary>
public static class LogFields
{
    public const string Ts = "ts";
    public const string Level = "level";
    public const string Event = "event";
    public const string Tool = "tool";
    public const string CallId = "call_id";
    public const string ErrorCode = "error_code";
    public const string DurationMs = "duration_ms";
    public const string GitExitCode = "git_exit_code";
    public const string ArgvLen = "argv_len";
    public const string CacheHit = "cache_hit";
    public const string Truncated = "truncated";
    public const string Message = "message";

    /// <summary>
    /// On <c>subprocess_exit</c>, the last 512 bytes of git's stderr with control characters stripped. On an event that
    /// carries an exception, the exception's type chain, outer to inner; never its message or stack trace, which can carry
    /// an absolute path.
    /// </summary>
    public const string StderrTail = "stderr_tail";

    // Server-controlled session metrics. Emitted only on shutdown.
    public const string MetricsSummary = "metrics_summary";

    // The text after "git version " in git's own output, allowlisted by GitVersionReader. Emitted only at startup.
    public const string GitVersion = "git_version";

    /// <summary>Service name property attached to every log line for downstream filtering.</summary>
    public const string Service = "service";

    public const string ServiceName = "mcp-gitops";

    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>
    {
        Ts, Level, Event, Tool, CallId, ErrorCode, DurationMs,
        GitExitCode, ArgvLen, CacheHit, Truncated, Message,
        StderrTail, MetricsSummary, GitVersion, Service,
    };
}