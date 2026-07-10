using RaccoonNinja.McpToolset.Server.FileVault.Domain;

namespace RaccoonNinja.McpToolset.Server.FileVault.Storage;

/// <summary>
/// A fully resolved view of one version of a file (metadata only — the caller reads the bytes
/// from the <see cref="FileStore"/> using <see cref="RelPath"/>).
/// </summary>
public sealed record FileRecord
{
    /// <summary>Project namespace the file belongs to.</summary>
    public string Project { get; init; }

    /// <summary>Name of the file.</summary>
    public string Name { get; init; }

    /// <summary>Content format.</summary>
    public VaultFormat Format { get; init; }

    /// <summary>
    /// The summary for this record: the live file summary for a current-version read, or the
    /// version-captured summary when a historical version was requested.
    /// </summary>
    public string Summary { get; init; }

    /// <summary>Tags attached to the file, sorted.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>The version this record describes.</summary>
    public int Version { get; init; }

    /// <summary>blake3 content hash of this version.</summary>
    public ContentHash Hash { get; init; }

    /// <summary>Snapshot path relative to the file-store root.</summary>
    public string RelPath { get; init; }

    /// <summary>Size of this version's content in bytes.</summary>
    public long ByteSize { get; init; }

    /// <summary>Current lifecycle state of the file.</summary>
    public FileState State { get; init; }

    /// <summary>The parent file's name, or <c>null</c> for top-level files.</summary>
    public string Parent { get; init; }

    /// <summary>
    /// The file's current version (the conflict token), which may differ from
    /// <see cref="Version"/> when a historical version was requested.
    /// </summary>
    public int CurrentVersion { get; init; }
}