using RaccoonNinja.McpToolset.Server.FileVault.Domain;

namespace RaccoonNinja.McpToolset.Server.FileVault.Storage;

/// <summary>The outcome of a successful commit.</summary>
public sealed record Committed
{
    /// <summary>The version that was written.</summary>
    public int Version { get; init; }

    /// <summary>blake3 content hash of the committed content.</summary>
    public ContentHash Hash { get; init; }

    /// <summary>
    /// Length of the committed full content in UTF-16 code units. Set by the service after the
    /// repository commit; the storage layer never sees the decoded string.
    /// </summary>
    public int ContentChars { get; init; }

    /// <summary>
    /// Advisory split hint composed by the service when <see cref="ContentChars"/> exceeds
    /// the configured threshold; null otherwise. Never set by the storage layer.
    /// </summary>
    public string SplitHint { get; init; }
}