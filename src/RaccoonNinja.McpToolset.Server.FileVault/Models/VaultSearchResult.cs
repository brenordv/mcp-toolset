using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.FileVault.Models;

/// <summary>Result of <c>vault_search</c>.</summary>
public sealed record VaultSearchResult
{
    /// <summary>The matched notes for this response, ranked and capped to the limit.</summary>
    [JsonPropertyName("items")]
    public IReadOnlyList<VaultSearchItem> Items { get; init; } = [];

    /// <summary>The number of items returned.</summary>
    [JsonPropertyName("count")]
    public int Count { get; init; }

    /// <summary>Whether more matches existed beyond the returned items.</summary>
    [JsonPropertyName("truncated")]
    public bool Truncated { get; init; }

    /// <summary>Which matching pass produced these results (<c>all_terms</c> or <c>any_term_fallback</c>).</summary>
    [JsonPropertyName("query_mode")]
    public string QueryMode { get; init; }

    /// <summary>How many notes were skipped because their snapshot could not be read (normally 0).</summary>
    [JsonPropertyName("skipped")]
    public int Skipped { get; init; }
}