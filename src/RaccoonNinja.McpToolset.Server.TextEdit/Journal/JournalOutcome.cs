namespace RaccoonNinja.McpToolset.Server.TextEdit.Journal;

/// <summary>
/// The stored outcome of a journaled file row. The journal records only files a batch will actually
/// rewrite; a refused file is a no-op reported in the tool result, not a journal row. A row is written
/// <see cref="Pending"/> before any disk write and flipped to <see cref="Changed"/> after, so a row still
/// <see cref="Pending"/> after a crash marks a write that undo can still roll back from its pre-image.
/// </summary>
public static class JournalOutcome
{
    /// <summary>The pre-image is recorded but the disk write is not yet confirmed (write-ahead, pre-write).</summary>
    public const string Pending = "pending";

    /// <summary>The disk write completed and the post-image hash is recorded.</summary>
    public const string Changed = "changed";
}