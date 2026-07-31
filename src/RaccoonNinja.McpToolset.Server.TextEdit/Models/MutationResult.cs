using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Models;

/// <summary>
/// The payload of <c>normalize_files</c> and <c>replace_text</c>: the batch id (present only when files were
/// actually written), the attempted/changed/refused counts, and the per-file entries. On a <c>dry_run</c>
/// the batch id is absent and each changed file carries a diff.
/// </summary>
public sealed record MutationResult
{
    /// <summary>The committed journal batch id, or <c>null</c> for a dry run or a batch that changed nothing.</summary>
    [JsonPropertyName("batch_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? BatchId { get; init; }

    /// <summary>Whether the call was a dry run (nothing was written).</summary>
    [JsonPropertyName("dry_run")]
    public bool DryRun { get; init; }

    /// <summary>How many files the batch attempted.</summary>
    [JsonPropertyName("attempted")]
    public int Attempted { get; init; }

    /// <summary>How many files the batch changed.</summary>
    [JsonPropertyName("changed")]
    public int Changed { get; init; }

    /// <summary>How many files the batch refused.</summary>
    [JsonPropertyName("refused")]
    public int Refused { get; init; }

    /// <summary>The per-file outcomes, in selection order.</summary>
    [JsonPropertyName("files")]
    public IReadOnlyList<FileChange> Files { get; init; } = [];
}