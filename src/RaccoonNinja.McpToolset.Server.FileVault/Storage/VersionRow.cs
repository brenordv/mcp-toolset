using RaccoonNinja.McpToolset.Server.FileVault.Domain;

namespace RaccoonNinja.McpToolset.Server.FileVault.Storage;

/// <summary>One row of version history for <c>vault_history</c>.</summary>
public sealed record VersionRow
{
    /// <summary>The version this row describes.</summary>
    public int Version { get; init; }

    /// <summary>The kind of write that produced this version.</summary>
    public VaultOp Op { get; init; }

    /// <summary>The summary captured when this version was written.</summary>
    public string Summary { get; init; }

    /// <summary>Size of this version's content in bytes.</summary>
    public long ByteSize { get; init; }

    /// <summary>blake3 content hash of this version.</summary>
    public ContentHash Hash { get; init; }

    /// <summary>Creation timestamp, Unix epoch seconds.</summary>
    public long CreatedAt { get; init; }
}