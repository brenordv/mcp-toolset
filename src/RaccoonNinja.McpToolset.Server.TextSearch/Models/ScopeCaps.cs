using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Models;

/// <summary>The effective caps reported by <c>describe_scope</c>.</summary>
public sealed record ScopeCaps
{
    /// <summary>The default per-call file result cap.</summary>
    [JsonPropertyName("max_files_default")]
    public int MaxFilesDefault { get; init; }

    /// <summary>The hard ceiling the per-call file cap is clamped to (also the aggregate window across roots).</summary>
    [JsonPropertyName("max_files_ceiling")]
    public int MaxFilesCeiling { get; init; }

    /// <summary>The maximum file size read or inspected, in bytes.</summary>
    [JsonPropertyName("max_file_bytes")]
    public long MaxFileBytes { get; init; }

    /// <summary>The ceiling on total search matches per call.</summary>
    [JsonPropertyName("max_results")]
    public int MaxResults { get; init; }

    /// <summary>The ceiling on matches per file.</summary>
    [JsonPropertyName("max_matches_per_file")]
    public int MaxMatchesPerFile { get; init; }

    /// <summary>The ceiling on context lines around a match.</summary>
    [JsonPropertyName("max_context_lines")]
    public int MaxContextLines { get; init; }

    /// <summary>The ceiling on the line span a single <c>read_lines</c> call returns.</summary>
    [JsonPropertyName("max_line_span")]
    public int MaxLineSpan { get; init; }

    /// <summary>The per-match regex timeout, in milliseconds.</summary>
    [JsonPropertyName("regex_timeout_ms")]
    public int RegexTimeoutMs { get; init; }

    /// <summary>The whole-operation wall-clock budget, in milliseconds.</summary>
    [JsonPropertyName("operation_budget_ms")]
    public int OperationBudgetMs { get; init; }
}