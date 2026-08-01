using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Models;

/// <summary>The payload of <c>undo_batch</c>/<c>undo_last_batch</c>: the restored, recreated, and skipped files.</summary>
public sealed record UndoResult
{
    /// <summary>The batch that was undone.</summary>
    [JsonPropertyName("batch_id")]
    public long BatchId { get; init; }

    /// <summary>The root-relative paths restored to their pre-image.</summary>
    [JsonPropertyName("restored")]
    public IReadOnlyList<string> Restored { get; init; } = [];

    /// <summary>The root-relative paths recreated because the file had been deleted since the batch.</summary>
    [JsonPropertyName("recreated")]
    public IReadOnlyList<string> Recreated { get; init; } = [];

    /// <summary>The files skipped (out of root, denylisted, changed since, or unreadable), each named.</summary>
    [JsonPropertyName("skipped")]
    public IReadOnlyList<SkippedFile> Skipped { get; init; } = [];
}