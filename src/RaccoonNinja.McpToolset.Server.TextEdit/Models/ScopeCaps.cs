using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Models;

/// <summary>The effective caps reported by <c>describe_scope</c>.</summary>
public sealed record ScopeCaps
{
    /// <summary>The default per-call file result cap for a selector.</summary>
    [JsonPropertyName("max_files_default")]
    public int MaxFilesDefault { get; init; }

    /// <summary>The hard ceiling the per-call file cap is clamped to.</summary>
    [JsonPropertyName("max_files_ceiling")]
    public int MaxFilesCeiling { get; init; }

    /// <summary>The maximum file size read or rewritten, in bytes.</summary>
    [JsonPropertyName("max_file_bytes")]
    public long MaxFileBytes { get; init; }

    /// <summary>The per-match regex timeout, in milliseconds.</summary>
    [JsonPropertyName("regex_timeout_ms")]
    public int RegexTimeoutMs { get; init; }

    /// <summary>The whole-operation wall-clock budget, in milliseconds.</summary>
    [JsonPropertyName("operation_budget_ms")]
    public int OperationBudgetMs { get; init; }

    /// <summary>The minimum detection confidence to rewrite a file without an explicit source encoding.</summary>
    [JsonPropertyName("rewrite_confidence")]
    public double RewriteConfidence { get; init; }

    /// <summary>The maximum length, in characters, of an agent-supplied regex pattern.</summary>
    [JsonPropertyName("pattern_length_cap")]
    public int PatternLengthCap { get; init; }

    /// <summary>The number of most-recent batches the journal retains.</summary>
    [JsonPropertyName("journal_retention_batches")]
    public int JournalRetentionBatches { get; init; }

    /// <summary>The age, in hours, past which a journal batch is eligible for pruning.</summary>
    [JsonPropertyName("journal_retention_hours")]
    public int JournalRetentionHours { get; init; }
}