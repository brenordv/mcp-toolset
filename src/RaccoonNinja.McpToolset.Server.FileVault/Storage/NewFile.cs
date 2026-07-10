using RaccoonNinja.McpToolset.Server.FileVault.Domain;

namespace RaccoonNinja.McpToolset.Server.FileVault.Storage;

/// <summary>Everything needed to create a brand-new file at version 1.</summary>
public sealed record NewFile
{
    /// <summary>Project namespace the file belongs to.</summary>
    public string Project { get; init; }

    /// <summary>Name of the file.</summary>
    public string Name { get; init; }

    /// <summary>Content format.</summary>
    public VaultFormat Format { get; init; }

    /// <summary>One-line file summary.</summary>
    public string Summary { get; init; }

    /// <summary>Tags for filtering and categorization.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>blake3 content hash for integrity checks and the snapshot filename.</summary>
    public ContentHash Hash { get; init; }

    /// <summary>Snapshot path relative to the file-store root (<c>files/</c>).</summary>
    public string RelPath { get; init; }

    /// <summary>Size of the content in bytes.</summary>
    public long ByteSize { get; init; }

    /// <summary>Optional parent file name (same project) to link under; <c>null</c> = top-level.</summary>
    public string Parent { get; init; }

    /// <summary>Creation timestamp, Unix epoch seconds.</summary>
    public long CreatedAt { get; init; }
}