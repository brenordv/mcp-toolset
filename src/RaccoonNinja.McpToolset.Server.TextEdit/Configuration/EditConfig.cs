using System.Globalization;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Configuration;

/// <summary>
/// Resolved runtime caps for one server process. Every cap has an environment override and is surfaced
/// through <c>describe_scope</c>. The confinement root itself is owned by <see cref="RootRegistry"/>, not
/// here.
/// </summary>
public sealed record EditConfig
{
    /// <summary>Default result cap for a single selector call.</summary>
    public const int DefaultMaxFiles = 1_000;

    /// <summary>Hard ceiling the per-call <c>max_files</c> is clamped to.</summary>
    public const int DefaultMaxFilesCeiling = 10_000;

    /// <summary>Default maximum file size read or rewritten, in bytes (5 MiB).</summary>
    public const long DefaultMaxFileBytes = 5L * 1024 * 1024;

    /// <summary>Default per-match regex timeout, in milliseconds.</summary>
    public const int DefaultRegexTimeoutMs = 1_000;

    /// <summary>Default wall-clock budget for one whole operation across the file set, in milliseconds.</summary>
    public const int DefaultOperationBudgetMs = 30_000;

    /// <summary>Default minimum detection confidence to rewrite a file without an explicit source encoding.</summary>
    public const double DefaultRewriteConfidence = 0.65;

    /// <summary>Default maximum length, in characters, of an agent-supplied regex pattern.</summary>
    public const int DefaultPatternLengthCap = 2_048;

    /// <summary>Default number of most-recent batches the journal retains.</summary>
    public const int DefaultJournalRetentionBatches = 50;

    /// <summary>Default age, in hours, past which a journal batch is eligible for pruning.</summary>
    public const int DefaultJournalRetentionHours = 48;

    /// <summary>The default per-call file result cap.</summary>
    public int MaxFilesDefault { get; init; }

    /// <summary>The hard ceiling the per-call file cap is clamped to.</summary>
    public int MaxFilesCeiling { get; init; }

    /// <summary>The maximum file size read or rewritten, in bytes.</summary>
    public long MaxFileBytes { get; init; }

    /// <summary>The per-match regex timeout.</summary>
    public TimeSpan RegexTimeout { get; init; }

    /// <summary>The wall-clock budget for one whole operation across the file set.</summary>
    public TimeSpan OperationBudget { get; init; }

    /// <summary>The minimum detection confidence to rewrite a file without an explicit source encoding.</summary>
    public double RewriteConfidence { get; init; }

    /// <summary>The maximum length, in characters, of an agent-supplied regex pattern.</summary>
    public int PatternLengthCap { get; init; }

    /// <summary>The number of most-recent batches the journal retains.</summary>
    public int JournalRetentionBatches { get; init; }

    /// <summary>The age, in hours, past which a journal batch is eligible for pruning.</summary>
    public int JournalRetentionHours { get; init; }

    /// <summary>Resolve the caps from the process environment.</summary>
    /// <returns>The resolved configuration.</returns>
    /// <exception cref="EditStartupException">Thrown when a numeric override is present but invalid.</exception>
    public static EditConfig Load()
        => new()
        {
            MaxFilesDefault = ParseInt("MCP_TEXTEDIT_MAX_FILES", DefaultMaxFiles),
            MaxFilesCeiling = ParseInt("MCP_TEXTEDIT_MAX_FILES_CEILING", DefaultMaxFilesCeiling),
            MaxFileBytes = ParseLong("MCP_TEXTEDIT_MAX_FILE_BYTES", DefaultMaxFileBytes),
            RegexTimeout = TimeSpan.FromMilliseconds(ParseInt("MCP_TEXTEDIT_REGEX_TIMEOUT_MS", DefaultRegexTimeoutMs)),
            OperationBudget = TimeSpan.FromMilliseconds(ParseInt("MCP_TEXTEDIT_OP_BUDGET_MS", DefaultOperationBudgetMs)),
            RewriteConfidence = ParseConfidence("MCP_TEXTEDIT_REWRITE_CONFIDENCE", DefaultRewriteConfidence),
            PatternLengthCap = ParseInt("MCP_TEXTEDIT_PATTERN_LENGTH_CAP", DefaultPatternLengthCap),
            JournalRetentionBatches = ParseInt("MCP_TEXTEDIT_JOURNAL_RETENTION_BATCHES", DefaultJournalRetentionBatches),
            JournalRetentionHours = ParseInt("MCP_TEXTEDIT_JOURNAL_RETENTION_HOURS", DefaultJournalRetentionHours),
        };

    /// <summary>A short, path-free summary of the effective caps for the startup scope log line.</summary>
    /// <returns>The cap summary.</returns>
    public string CapsSummary()
        => string.Create(
            CultureInfo.InvariantCulture,
            $"maxFiles={MaxFilesDefault}/{MaxFilesCeiling} maxFileBytes={MaxFileBytes} regexTimeoutMs={(int)RegexTimeout.TotalMilliseconds} opBudgetMs={(int)OperationBudget.TotalMilliseconds} rewriteConfidence={RewriteConfidence} patternLenCap={PatternLengthCap} journalRetention={JournalRetentionBatches}/{JournalRetentionHours}h");

    private static int ParseInt(string key, int defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : throw new EditStartupException($"{key}='{raw}' is not a valid positive integer");
    }

    private static long ParseLong(string key, long defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : throw new EditStartupException($"{key}='{raw}' is not a valid positive byte count");
    }

    private static double ParseConfidence(string key, double defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed is > 0 and <= 1
            ? parsed
            : throw new EditStartupException($"{key}='{raw}' is not a confidence in the range (0, 1]");
    }
}