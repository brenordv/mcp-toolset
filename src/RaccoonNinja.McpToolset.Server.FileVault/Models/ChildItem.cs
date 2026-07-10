using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.FileVault.Models;

/// <summary>One related child note in a <c>vault_get</c> result.</summary>
public sealed record ChildItem
{
    /// <summary>The child note's name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; }

    /// <summary>The child note's current one-line summary.</summary>
    [JsonPropertyName("summary")]
    public string Summary { get; init; }
}