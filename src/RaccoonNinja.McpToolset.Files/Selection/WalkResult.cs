namespace RaccoonNinja.McpToolset.Files.Selection;

/// <summary>
/// The outcome of a <see cref="FileWalker"/> run: the matched entries in a deterministic order plus the
/// two counters a caller needs to be honest about what it did not return, an aggregate count of skipped
/// symbolic links (so pruned symlinks don't read as "missing" files) and whether the result was capped.
/// </summary>
public sealed record WalkResult
{
    /// <summary>The matched entries, sorted ordinal by <see cref="WalkEntry.RelativePath"/>.</summary>
    public IReadOnlyList<WalkEntry> Entries { get; init; } = [];

    /// <summary>How many symbolic-link or junction entries were skipped (never descended, never returned).</summary>
    public int SkippedSymlinks { get; init; }

    /// <summary>Whether the walk was cut short by the result cap or the visited-node cap; more entries may exist.</summary>
    public bool Truncated { get; init; }
}