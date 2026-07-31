namespace RaccoonNinja.McpToolset.Server.TextEdit.Logging;

/// <summary>
/// The fixed field allowlist for the logging contract. Any key not listed here is dropped at format
/// time (see <see cref="AllowlistJsonFormatter"/>), so a future bug cannot leak an unexpected value,
/// above all an absolute path, into the log sink.
/// </summary>
public static class LogFields
{
    /// <summary>ISO-8601 UTC timestamp.</summary>
    public const string Ts = "ts";

    /// <summary>Log level, uppercase.</summary>
    public const string Level = "level";

    /// <summary>The lifecycle or per-call event name (for example <c>tool_error</c>, <c>refusal</c>).</summary>
    public const string Event = "event";

    /// <summary>The tool the record belongs to, or <c>server</c> for lifecycle records.</summary>
    public const string Tool = "tool";

    /// <summary>The monotonic per-call correlation id.</summary>
    public const string CallId = "call_id";

    /// <summary>The error code carried on a failure record.</summary>
    public const string ErrorCode = "error_code";

    /// <summary>A wall-clock duration in milliseconds.</summary>
    public const string DurationMs = "duration_ms";

    /// <summary>Whether the result was capped.</summary>
    public const string Truncated = "truncated";

    /// <summary>The reason a write or match was refused (for example <c>denylisted</c>, <c>out_of_root</c>, <c>ignored</c>).</summary>
    public const string RefusalReason = "refusal_reason";

    /// <summary>Whether an agent regex fell back from the non-backtracking engine to backtracking.</summary>
    public const string RegexFallback = "regex_fallback";

    /// <summary>How many files a single operation opened.</summary>
    public const string FilesScanned = "files_scanned";

    /// <summary>An 8-char hash of the canonical root, so records correlate without leaking the path.</summary>
    public const string RootHash = "root_hash";

    /// <summary>The journal batch id a mutation or undo record belongs to.</summary>
    public const string BatchId = "batch_id";

    /// <summary>How many files a batch attempted.</summary>
    public const string Attempted = "attempted";

    /// <summary>How many files a batch changed.</summary>
    public const string Changed = "changed";

    /// <summary>How many files a batch refused.</summary>
    public const string Refused = "refused";

    /// <summary>Whether the call was a dry run (no write performed).</summary>
    public const string DryRun = "dry_run";

    /// <summary>The detected encoding name for a rewritten file.</summary>
    public const string Encoding = "encoding";

    /// <summary>The encoding-detection ladder step that decided a file's encoding.</summary>
    public const string LadderStep = "ladder_step";

    /// <summary>Whether an undo target was skipped because its current hash no longer matched the recorded post-image.</summary>
    public const string UndoMismatch = "undo_mismatch";

    /// <summary>The free-text message.</summary>
    public const string Message = "message";

    /// <summary>A capped, control-stripped exception tail; the only field allowed to carry raw text.</summary>
    public const string ExceptionTail = "exception_tail";

    /// <summary>The session metrics snapshot, emitted only on shutdown.</summary>
    public const string MetricsSummary = "metrics_summary";

    /// <summary>The service name attached to every record for downstream filtering.</summary>
    public const string Service = "service";

    /// <summary>The service name value.</summary>
    public const string ServiceName = "mcp-text-edit";

    /// <summary>The set of keys the formatter will emit; everything else is dropped.</summary>
    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
    {
        Ts, Level, Event, Tool, CallId, ErrorCode, DurationMs, Truncated,
        RefusalReason, RegexFallback, FilesScanned, RootHash, BatchId, Attempted,
        Changed, Refused, DryRun, Encoding, LadderStep, UndoMismatch, Message,
        ExceptionTail, MetricsSummary, Service,
    };
}