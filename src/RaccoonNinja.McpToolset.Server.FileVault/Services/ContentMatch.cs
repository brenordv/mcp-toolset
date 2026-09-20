using RaccoonNinja.McpToolset.Server.FileVault.Storage;

namespace RaccoonNinja.McpToolset.Server.FileVault.Services;

/// <summary>A note whose body matched a <c>vault_search</c> query, with its matched terms and snippets.</summary>
public sealed record ContentMatch
{
    /// <summary>The matched note.</summary>
    public SearchCandidateRow Candidate { get; init; }

    /// <summary>The distinct query terms whose text appears in the body, in first-occurrence order.</summary>
    public IReadOnlyList<string> MatchedTerms { get; init; } = [];

    /// <summary>The windowed snippets, at most one per matched term and capped per note.</summary>
    public IReadOnlyList<ContentSnippet> Snippets { get; init; } = [];
}