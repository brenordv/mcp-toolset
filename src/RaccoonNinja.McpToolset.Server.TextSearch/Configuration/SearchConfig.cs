using System.Globalization;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Configuration;

/// <summary>
/// Resolved runtime caps for one server process. Every cap has an environment override and is surfaced
/// through <c>describe_scope</c>. The base root and per-call scope resolution are owned by
/// <see cref="ScopeResolver"/>, not here.
/// </summary>
public sealed record SearchConfig
{
    /// <summary>Default result cap for a single call.</summary>
    public const int DefaultMaxFiles = 1_000;

    /// <summary>Hard ceiling the per-call <c>max_files</c> is clamped to.</summary>
    public const int DefaultMaxFilesCeiling = 10_000;

    /// <summary>Default maximum file size read or inspected, in bytes (5 MiB).</summary>
    public const long DefaultMaxFileBytes = 5L * 1024 * 1024;

    /// <summary>Default ceiling on total search matches returned in one call.</summary>
    public const int DefaultMaxResults = 10_000;

    /// <summary>Default ceiling on matches returned per file.</summary>
    public const int DefaultMaxMatchesPerFile = 1_000;

    /// <summary>Default ceiling on context lines around a match.</summary>
    public const int DefaultMaxContextLines = 50;

    /// <summary>Default ceiling on the line span a single <c>read_lines</c> call may return.</summary>
    public const int DefaultMaxLineSpan = 5_000;

    /// <summary>Default per-match regex timeout, in milliseconds.</summary>
    public const int DefaultRegexTimeoutMs = 1_000;

    /// <summary>Default wall-clock budget for one whole operation across the file set, in milliseconds.</summary>
    public const int DefaultOperationBudgetMs = 30_000;

    /// <summary>The default per-call result cap.</summary>
    public int MaxFilesDefault { get; init; }

    /// <summary>The hard ceiling the per-call result cap is clamped to.</summary>
    public int MaxFilesCeiling { get; init; }

    /// <summary>The maximum file size read or inspected, in bytes.</summary>
    public long MaxFileBytes { get; init; }

    /// <summary>The ceiling on total search matches returned in one call.</summary>
    public int MaxResults { get; init; }

    /// <summary>The ceiling on matches returned per file.</summary>
    public int MaxMatchesPerFile { get; init; }

    /// <summary>The ceiling on context lines around a match.</summary>
    public int MaxContextLines { get; init; }

    /// <summary>The ceiling on the line span a single <c>read_lines</c> call may return.</summary>
    public int MaxLineSpan { get; init; }

    /// <summary>The per-match regex timeout.</summary>
    public TimeSpan RegexTimeout { get; init; }

    /// <summary>The wall-clock budget for one whole operation across the file set.</summary>
    public TimeSpan OperationBudget { get; init; }

    /// <summary>Whether content-based secret detection withholds files whose content matches a detector (default true).</summary>
    public bool SecretScanEnabled { get; init; } = true;

    /// <summary>Whether the higher-false-positive aggressive detector layer is enabled (default false).</summary>
    public bool SecretScanAggressive { get; init; }

    /// <summary>Resolve the caps from the process environment.</summary>
    /// <returns>The resolved configuration.</returns>
    /// <exception cref="SearchStartupException">Thrown when a numeric override is present but invalid.</exception>
    public static SearchConfig Load()
    {
        var (secretScanEnabled, secretScanAggressive) = ParseSecretScan("MCP_TEXTSEARCH_SECRET_SCAN");
        return new SearchConfig
        {
            MaxFilesDefault = ParseInt("MCP_TEXTSEARCH_MAX_FILES", DefaultMaxFiles),
            MaxFilesCeiling = ParseInt("MCP_TEXTSEARCH_MAX_FILES_CEILING", DefaultMaxFilesCeiling),
            MaxFileBytes = ParseLong("MCP_TEXTSEARCH_MAX_FILE_BYTES", DefaultMaxFileBytes),
            MaxResults = ParseInt("MCP_TEXTSEARCH_MAX_RESULTS", DefaultMaxResults),
            MaxMatchesPerFile = ParseInt("MCP_TEXTSEARCH_MAX_MATCHES_PER_FILE", DefaultMaxMatchesPerFile),
            MaxContextLines = ParseInt("MCP_TEXTSEARCH_MAX_CONTEXT_LINES", DefaultMaxContextLines),
            MaxLineSpan = ParseInt("MCP_TEXTSEARCH_MAX_LINE_SPAN", DefaultMaxLineSpan),
            RegexTimeout = TimeSpan.FromMilliseconds(ParseInt("MCP_TEXTSEARCH_REGEX_TIMEOUT_MS", DefaultRegexTimeoutMs)),
            OperationBudget = TimeSpan.FromMilliseconds(ParseInt("MCP_TEXTSEARCH_OP_BUDGET_MS", DefaultOperationBudgetMs)),
            SecretScanEnabled = secretScanEnabled,
            SecretScanAggressive = secretScanAggressive,
        };
    }

    /// <summary>A short, path-free summary of the effective caps for the startup scope log line.</summary>
    /// <returns>The cap summary.</returns>
    public string CapsSummary()
        => string.Create(
            CultureInfo.InvariantCulture,
            $"maxFiles={MaxFilesDefault}/{MaxFilesCeiling} maxFileBytes={MaxFileBytes} maxResults={MaxResults} maxMatchesPerFile={MaxMatchesPerFile} maxContextLines={MaxContextLines} maxLineSpan={MaxLineSpan} regexTimeoutMs={(int)RegexTimeout.TotalMilliseconds} opBudgetMs={(int)OperationBudget.TotalMilliseconds}");

    private static int ParseInt(string key, int defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : throw new SearchStartupException($"{key}='{raw}' is not a valid positive integer");
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
            : throw new SearchStartupException($"{key}='{raw}' is not a valid positive byte count");
    }

    private static (bool Enabled, bool Aggressive) ParseSecretScan(string key)
    {
        var raw = Environment.GetEnvironmentVariable(key)?.Trim().ToLowerInvariant();
        return raw switch
        {
            null or "" or "on" or "true" => (true, false),
            "off" or "false" => (false, false),
            "aggressive" => (true, true),
            _ => throw new SearchStartupException($"{key}='{raw}' must be one of: on, off, aggressive"),
        };
    }
}