using RaccoonNinja.McpToolset.Server.FileVault.Domain;

namespace RaccoonNinja.McpToolset.Server.FileVault.Storage;

/// <summary>
/// An optimistic-concurrency write of the next version of an existing file. The snapshot at
/// <see cref="RelPath"/> has already been written to disk; the repository performs the version
/// check and commit atomically.
/// </summary>
public sealed record VersionedWrite
{
    /// <summary>Project namespace the file belongs to.</summary>
    public string Project { get; init; }

    /// <summary>Name of the file.</summary>
    public string Name { get; init; }

    /// <summary>The version the caller derived this write from.</summary>
    public int BaseVersion { get; init; }

    /// <summary>The version this write will create (<see cref="BaseVersion"/> + 1).</summary>
    public int NewVersion { get; init; }

    /// <summary>Content format.</summary>
    public VaultFormat Format { get; init; }

    /// <summary>Non-null replaces the file summary; <c>null</c> keeps the existing one (append/edit).</summary>
    public string Summary { get; init; }

    /// <summary>Non-null replaces the tag set; <c>null</c> keeps the existing tags.</summary>
    public IReadOnlyList<string> Tags { get; init; }

    /// <summary>blake3 content hash for integrity checks and the snapshot filename.</summary>
    public ContentHash Hash { get; init; }

    /// <summary>Snapshot path relative to the file-store root.</summary>
    public string RelPath { get; init; }

    /// <summary>Size of this version's content in bytes.</summary>
    public long ByteSize { get; init; }

    /// <summary>The kind of write that produced this version.</summary>
    public VaultOp Op { get; init; }

    /// <summary>Parent-link change to apply (save passes leave/set; append/edit pass leave).</summary>
    public ParentUpdate Parent { get; init; } = ParentUpdate.Leave;

    /// <summary>Update timestamp, Unix epoch seconds.</summary>
    public long UpdatedAt { get; init; }
}