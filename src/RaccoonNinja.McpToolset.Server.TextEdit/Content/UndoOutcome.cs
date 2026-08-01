namespace RaccoonNinja.McpToolset.Server.TextEdit.Content;

/// <summary>The result of undoing one batch: the files restored, the since-deleted files recreated, and the rows skipped and named.</summary>
public sealed record UndoOutcome
{
    /// <summary>The batch that was undone.</summary>
    public long BatchId { get; init; }

    /// <summary>The root-relative paths restored to their pre-image.</summary>
    public IReadOnlyList<string> Restored { get; init; } = [];

    /// <summary>The root-relative paths recreated because the file had been deleted since the batch.</summary>
    public IReadOnlyList<string> Recreated { get; init; } = [];

    /// <summary>The rows skipped (out of root, denylisted, changed since, or unreadable), each named.</summary>
    public IReadOnlyList<SkippedUndo> Skipped { get; init; } = [];
}