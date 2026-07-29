namespace RaccoonNinja.McpToolset.Server.TextSearch.Content;

/// <summary>The outcome of searching one file: its matches and whether they were cut short.</summary>
/// <param name="Matches">The matches found, in line then column order.</param>
/// <param name="TimedOut">Whether a regex match timed out; matching stopped for this file.</param>
/// <param name="CappedPerFile">Whether the per-file match cap was reached.</param>
public sealed record FileSearchOutcome(
    IReadOnlyList<ContentMatch> Matches,
    bool TimedOut,
    bool CappedPerFile);