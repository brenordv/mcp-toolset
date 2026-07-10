using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.FileVault.Models;

/// <summary>One row in a <c>vault_history</c> result.</summary>
public sealed record HistoryItem
{
    /// <summary>The version this row describes.</summary>
    [JsonPropertyName("version")]
    public int Version { get; init; }

    /// <summary>The kind of write that produced this version.</summary>
    [JsonPropertyName("op")]
    public string Op { get; init; }

    /// <summary>The summary captured when this version was written.</summary>
    [JsonPropertyName("summary")]
    public string Summary { get; init; }

    /// <summary>Size of this version's content in bytes.</summary>
    [JsonPropertyName("byte_size")]
    public long ByteSize { get; init; }

    /// <summary>blake3 content hash of this version, hex-encoded.</summary>
    [JsonPropertyName("content_hash")]
    public string ContentHash { get; init; }

    /// <summary>Creation timestamp, Unix epoch seconds.</summary>
    [JsonPropertyName("created_at")]
    public long CreatedAt { get; init; }
}