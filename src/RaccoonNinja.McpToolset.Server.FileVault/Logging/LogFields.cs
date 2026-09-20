namespace RaccoonNinja.McpToolset.Server.FileVault.Logging;

/// <summary>
/// The fixed field allowlist defined by the vault logging contract. Loggable fields are validated
/// identifiers and system data (project, name, paths, versions, sizes, counts, durations, error
/// codes, exception types); all free text (content, summaries, tags, diffs, headings, key paths,
/// FTS queries) is never loggable. Any key not named here is dropped at format time so a future
/// bug cannot silently leak a redacted value.
///
/// Never loggable, and never to be added here: query text (only its token count, byte length, and
/// hash ride the log), search terms, <c>matched_terms</c>, snippet <c>term</c>/<c>text</c>, and
/// cursor contents (only an opaque <see cref="CursorHash"/> is logged).
/// </summary>
public static class LogFields
{
    public const string Ts = "ts";
    public const string Level = "level";
    public const string Event = "event";
    public const string Tool = "tool";
    public const string CallId = "call_id";
    public const string ErrorCode = "error_code";
    public const string ExceptionType = "exception_type";
    public const string DurationMs = "duration_ms";
    public const string Project = "project";
    public const string Name = "name";
    public const string BaseVersion = "base_version";
    public const string CurrentVersion = "current_version";
    public const string ContentSizeBytes = "content_size_bytes";
    public const string CommittedChars = "committed_chars";
    public const string SchemaVersion = "schema_version";
    public const string ActiveFiles = "active_files";
    public const string ArchivedFiles = "archived_files";
    public const string VersionRows = "version_rows";
    public const string DbSizeBytes = "db_size_bytes";
    public const string HomePath = "home_path";
    public const string MaxContentBytes = "max_content_bytes";
    public const string BusyTimeoutMs = "busy_timeout_ms";
    public const string SplitHintChars = "split_hint_chars";
    public const string MigrationsApplied = "migrations_applied";
    public const string QueryHash = "query_hash";
    public const string QueryTokens = "query_tokens";
    public const string QueryBytes = "query_bytes";
    public const string HeadingHash = "heading_hash";

    /// <summary>Which matching pass produced a query result (<c>all_terms</c> / <c>any_term_fallback</c>).</summary>
    public const string QueryMode = "query_mode";

    /// <summary>Item count on a returned <c>vault_list</c> page.</summary>
    public const string PageItems = "page_items";

    /// <summary>Whether more results existed beyond the returned page.</summary>
    public const string Truncated = "truncated";

    /// <summary>Opaque hash of a cursor argument (correlates a pagination walk without revealing it).</summary>
    public const string CursorHash = "cursor_hash";

    /// <summary>How many notes <c>vault_search</c> enumerated.</summary>
    public const string NotesScanned = "notes_scanned";

    /// <summary>How many notes matched a query.</summary>
    public const string NotesMatched = "notes_matched";

    /// <summary>Total bytes read across scanned snapshots.</summary>
    public const string BytesScanned = "bytes_scanned";

    /// <summary>How many notes were skipped because their snapshot could not be read.</summary>
    public const string SkippedUnreadable = "skipped_unreadable";

    /// <summary>The fixed reason category attached to an <c>invalid_argument</c> domain error.</summary>
    public const string Reason = "reason";

    public const string Message = "message";

    /// <summary>Exception text captured for non-domain failures (server-emitted, control-stripped).</summary>
    public const string StderrTail = "stderr_tail";

    /// <summary>Server-controlled session metrics. Emitted only on shutdown.</summary>
    public const string MetricsSummary = "metrics_summary";

    /// <summary>Service name property attached to every log line for downstream filtering.</summary>
    public const string Service = "service";

    public const string ServiceName = "mcp-filevault";

    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>
    {
        Ts, Level, Event, Tool, CallId, ErrorCode, ExceptionType, DurationMs,
        Project, Name, BaseVersion, CurrentVersion, ContentSizeBytes, CommittedChars,
        SchemaVersion, ActiveFiles, ArchivedFiles, VersionRows, DbSizeBytes,
        HomePath, MaxContentBytes, BusyTimeoutMs, SplitHintChars, MigrationsApplied,
        QueryHash, QueryTokens, QueryBytes, HeadingHash,
        QueryMode, PageItems, Truncated, CursorHash, NotesScanned, NotesMatched,
        BytesScanned, SkippedUnreadable, Reason,
        Message, StderrTail, MetricsSummary, Service,
    };
}