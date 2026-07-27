namespace RaccoonNinja.McpToolset.Files.Storage;

/// <summary>
/// A content-addressed blob store keyed by BLAKE3 digest. Blobs are sharded one level deep by the
/// first two hex characters so a single directory never accumulates every blob. Writes go through
/// <see cref="AtomicWriter"/>, so identical content is stored exactly once and a crash never leaves a
/// half-written blob. BLAKE3 (not SHA-256) matches the hash the file-vault store already uses.
/// </summary>
public sealed class BlobStore : IBlobStore
{
    private const int HexLength = 64;
    private const int ShardLength = 2;

    private readonly string _root;

    /// <summary>Create a store rooted at <paramref name="root"/>; the directory is created on first write.</summary>
    /// <param name="root">The blob storage root directory.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="root"/> is null or blank.</exception>
    public BlobStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    }

    /// <inheritdoc />
    public string Put(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var reference = Blake3.Hasher.Hash(content).ToString();
        AtomicWriter.WriteNew(PathFor(reference), content);
        return reference;
    }

    /// <inheritdoc />
    public byte[] Read(string blobRef)
        => File.ReadAllBytes(PathFor(Validated(blobRef)));

    /// <inheritdoc />
    public bool Exists(string blobRef)
        => File.Exists(PathFor(Validated(blobRef)));

    /// <inheritdoc />
    public void Remove(string blobRef)
    {
        var path = PathFor(Validated(blobRef));
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>Map a validated reference to its sharded on-disk path (<c>&lt;root&gt;/&lt;ab&gt;/&lt;full-hex&gt;</c>).</summary>
    private string PathFor(string reference)
        => Path.Combine(_root, reference[..ShardLength], reference);

    /// <summary>Ensure a reference is a lowercase 64-char hex digest; this also forecloses any path traversal through the ref.</summary>
    private static string Validated(string blobRef)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobRef);
        if (blobRef.Length != HexLength || !IsLowerHex(blobRef))
        {
            throw new ArgumentException($"'{blobRef}' is not a valid blob reference", nameof(blobRef));
        }

        return blobRef;
    }

    private static bool IsLowerHex(string value)
    {
        foreach (var c in value)
        {
            if (c is (< '0' or > '9') and (< 'a' or > 'f'))
            {
                return false;
            }
        }

        return true;
    }
}