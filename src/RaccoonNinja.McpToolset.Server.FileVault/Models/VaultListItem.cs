using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.FileVault.Models;

/// <summary>One row in a <c>vault_list</c> result.</summary>
public sealed record VaultListItem
{
    /// <summary>The file name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; }

    /// <summary>The project namespace the file lives in.</summary>
    [JsonPropertyName("project")]
    public string Project { get; init; }

    /// <summary>The file's one-line summary.</summary>
    [JsonPropertyName("summary")]
    public string Summary { get; init; }

    /// <summary>Tags attached to the file.</summary>
    [JsonPropertyName("tags")]
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>The file's current version.</summary>
    [JsonPropertyName("current_version")]
    public int CurrentVersion { get; init; }

    /// <summary>Last update timestamp, Unix epoch seconds.</summary>
    [JsonPropertyName("updated_at")]
    public long UpdatedAt { get; init; }

    /// <summary>The parent note's name, or <c>null</c> for top-level notes (emitted explicitly).</summary>
    [JsonPropertyName("parent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string Parent { get; init; }
}