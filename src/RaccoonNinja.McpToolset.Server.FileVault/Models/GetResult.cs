using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.FileVault.Models;

/// <summary>Result of <c>vault_get</c>.</summary>
public sealed record GetResult
{
    /// <summary>The file's content for the returned version (UTF-8).</summary>
    [JsonPropertyName("content")]
    public string Content { get; init; }

    /// <summary>Human-readable summary stored for the file.</summary>
    [JsonPropertyName("summary")]
    public string Summary { get; init; }

    /// <summary>Tags attached to the file.</summary>
    [JsonPropertyName("tags")]
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Content format.</summary>
    [JsonPropertyName("format")]
    public string Format { get; init; }

    /// <summary>The version returned.</summary>
    [JsonPropertyName("version")]
    public int Version { get; init; }

    /// <summary>blake3 content hash of this version, hex-encoded.</summary>
    [JsonPropertyName("content_hash")]
    public string ContentHash { get; init; }

    /// <summary>The file's current version; pass this back as <c>base_version</c> on your next write.</summary>
    [JsonPropertyName("current_version")]
    public int CurrentVersion { get; init; }

    /// <summary><c>true</c> if the file is archived.</summary>
    [JsonPropertyName("archived")]
    public bool Archived { get; init; }

    /// <summary>The parent note's name, or <c>null</c> for top-level notes (emitted explicitly).</summary>
    [JsonPropertyName("parent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string Parent { get; init; }

    /// <summary>The note's active children (related notes linked under it).</summary>
    [JsonPropertyName("children")]
    public IReadOnlyList<ChildItem> Children { get; init; } = [];
}