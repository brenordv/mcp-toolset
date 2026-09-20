using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.FileVault.Models;

/// <summary>One matched note in a <c>vault_search</c> result: metadata plus match context.</summary>
public sealed record VaultSearchItem
{
    /// <summary>The project namespace the note lives in.</summary>
    [JsonPropertyName("project")]
    public string Project { get; init; }

    /// <summary>The note name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; }

    /// <summary>The note's one-line summary.</summary>
    [JsonPropertyName("summary")]
    public string Summary { get; init; }

    /// <summary>The note's current version.</summary>
    [JsonPropertyName("current_version")]
    public int CurrentVersion { get; init; }

    /// <summary>Last update timestamp, Unix epoch seconds.</summary>
    [JsonPropertyName("updated_at")]
    public long UpdatedAt { get; init; }

    /// <summary>The query terms whose text appears in the body.</summary>
    [JsonPropertyName("matched_terms")]
    public IReadOnlyList<string> MatchedTerms { get; init; } = [];

    /// <summary>The windowed snippets around the matches, never the whole body.</summary>
    [JsonPropertyName("snippets")]
    public IReadOnlyList<VaultSearchSnippet> Snippets { get; init; } = [];
}