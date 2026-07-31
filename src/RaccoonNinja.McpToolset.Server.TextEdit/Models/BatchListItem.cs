using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Models;

/// <summary>One entry of <c>list_recent_batches</c>: an undoable batch and how many files it changed.</summary>
public sealed record BatchListItem
{
    /// <summary>The batch id to pass to <c>undo_batch</c>.</summary>
    [JsonPropertyName("batch_id")]
    public long BatchId { get; init; }

    /// <summary>The ISO-8601 UTC timestamp the batch was recorded.</summary>
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; init; }

    /// <summary>The tool that produced the batch.</summary>
    [JsonPropertyName("tool")]
    public string Tool { get; init; }

    /// <summary>How many files the batch changed (and undo would restore).</summary>
    [JsonPropertyName("changed")]
    public int Changed { get; init; }
}