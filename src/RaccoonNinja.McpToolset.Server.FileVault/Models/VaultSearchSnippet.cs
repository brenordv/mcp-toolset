using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.FileVault.Models;

/// <summary>One windowed snippet in a <c>vault_search</c> result item.</summary>
public sealed record VaultSearchSnippet
{
    /// <summary>The matched term this snippet was built around.</summary>
    [JsonPropertyName("term")]
    public string Term { get; init; }

    /// <summary>The windowed body text, with <c>…</c> markers where it was cut.</summary>
    [JsonPropertyName("text")]
    public string Text { get; init; }
}