namespace RaccoonNinja.McpToolset.Server.TextEdit.Journal;

/// <summary>
/// A compact view of a batch for <c>list_recent_batches</c>: its id, when it ran, the tool that produced
/// it, and how many files it changed (which is how many undo would restore). The journal records only
/// changed files, so the count is the file-row count.
/// </summary>
public sealed record BatchSummary
{
    /// <summary>The monotonic batch id.</summary>
    public long BatchId { get; init; }

    /// <summary>The ISO-8601 UTC timestamp the batch was recorded.</summary>
    public string CreatedUtc { get; init; }

    /// <summary>The tool that produced the batch.</summary>
    public string Tool { get; init; }

    /// <summary>How many files the batch changed (and undo would restore).</summary>
    public int ChangedCount { get; init; }
}