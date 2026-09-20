using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.FileVault.Models;

/// <summary>Result of <c>vault_list</c>.</summary>
public sealed record VaultListResult
{
    /// <summary>The matching files for this page, in canonical list order.</summary>
    [JsonPropertyName("items")]
    public IReadOnlyList<VaultListItem> Items { get; init; } = [];

    /// <summary>The number of items on this page.</summary>
    [JsonPropertyName("count")]
    public int Count { get; init; }

    /// <summary>Whether more items existed beyond this page.</summary>
    [JsonPropertyName("truncated")]
    public bool Truncated { get; init; }

    /// <summary>
    /// The cursor to pass back (with the other arguments unchanged) to fetch the next page.
    /// Omitted on the final page and in fallback mode, which never paginates.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string Cursor { get; init; }

    /// <summary>
    /// Which matching pass produced these results (<c>all_terms</c> or <c>any_term_fallback</c>).
    /// Omitted when no query was given.
    /// </summary>
    [JsonPropertyName("query_mode")]
    public string QueryMode { get; init; }
}