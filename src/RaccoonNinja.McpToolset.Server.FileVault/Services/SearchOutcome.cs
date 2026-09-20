using RaccoonNinja.McpToolset.Server.FileVault.Storage;

namespace RaccoonNinja.McpToolset.Server.FileVault.Services;

/// <summary>
/// The service-level result of <c>vault_search</c>: the page of matches plus the counters the tool
/// reports on the wire (<see cref="Count"/>, <see cref="Truncated"/>, <see cref="Mode"/>,
/// <see cref="Skipped"/>) and logs (<see cref="NotesScanned"/>, <see cref="BytesScanned"/>, and the
/// per-note <see cref="Skipped"/> diagnostics).
/// </summary>
public sealed record SearchOutcome
{
    /// <summary>The matches on this response, ranked and capped to the caller's limit.</summary>
    public IReadOnlyList<ContentMatch> Items { get; init; } = [];

    /// <summary>The number of matches returned in <see cref="Items"/>.</summary>
    public int Count { get; init; }

    /// <summary>Whether more matches existed beyond the returned page.</summary>
    public bool Truncated { get; init; }

    /// <summary>Which matching pass produced the results.</summary>
    public ListMode Mode { get; init; }

    /// <summary>How many active notes were enumerated (the scan breadth).</summary>
    public int NotesScanned { get; init; }

    /// <summary>Total bytes read across the scanned snapshots.</summary>
    public long BytesScanned { get; init; }

    /// <summary>Notes whose snapshot could not be read; also the source of the <c>skipped</c> count.</summary>
    public IReadOnlyList<SnapshotSkip> Skipped { get; init; } = [];
}