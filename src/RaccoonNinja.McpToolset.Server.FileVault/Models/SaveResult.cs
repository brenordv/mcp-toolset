using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.FileVault.Models;

/// <summary>Result of a versioned write (<c>save</c>/<c>append</c>/<c>edit_*</c>).</summary>
public sealed record SaveResult
{
    /// <summary>The version that was written.</summary>
    [JsonPropertyName("version")]
    public int Version { get; init; }

    /// <summary>blake3 hash of the committed content, hex-encoded.</summary>
    [JsonPropertyName("content_hash")]
    public string ContentHash { get; init; }

    /// <summary>
    /// Advisory nudge emitted when the committed content exceeds the configured split
    /// threshold; omitted from the wire otherwise. Distinct from the conflict diff hint,
    /// which travels on error results.
    /// </summary>
    [JsonPropertyName("hint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string SplitHint { get; init; }
}