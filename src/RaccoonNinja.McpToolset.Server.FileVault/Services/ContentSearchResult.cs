using RaccoonNinja.McpToolset.Server.FileVault.Storage;

namespace RaccoonNinja.McpToolset.Server.FileVault.Services;

/// <summary>
/// The ranked output of <see cref="ContentSearcher"/>: the selected matches plus the pass that
/// produced them. Not yet capped to a caller limit.
/// </summary>
public sealed record ContentSearchResult
{
    /// <summary>The selected matches, ranked (distinct terms desc, then the shared list ordering).</summary>
    public IReadOnlyList<ContentMatch> Matches { get; init; } = [];

    /// <summary>Which matching pass produced <see cref="Matches"/>.</summary>
    public ListMode Mode { get; init; }
}