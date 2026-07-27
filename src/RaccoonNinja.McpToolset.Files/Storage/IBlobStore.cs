namespace RaccoonNinja.McpToolset.Files.Storage;

/// <summary>
/// A content-addressed blob store: content is keyed by its BLAKE3 digest, so writing the same bytes
/// twice stores them once (deduplication for free) and a reference is simply that digest. It backs the
/// text-edit journal, where a pre-image snapshot taken before a mutation is stored once and referenced
/// by hash from the batch record.
/// </summary>
public interface IBlobStore
{
    /// <summary>Store <paramref name="content"/> and return its reference (the BLAKE3 digest as lowercase hex).</summary>
    /// <param name="content">The bytes to store.</param>
    /// <returns>The blob reference; identical content always yields the same reference.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="content"/> is null.</exception>
    /// <exception cref="IOException">Thrown when the blob cannot be written.</exception>
    string Put(byte[] content);

    /// <summary>Read the content previously stored under <paramref name="blobRef"/>.</summary>
    /// <param name="blobRef">A reference returned by <see cref="Put"/>.</param>
    /// <returns>The stored bytes.</returns>
    /// <exception cref="System.ArgumentException">Thrown when <paramref name="blobRef"/> is not a well-formed reference.</exception>
    /// <exception cref="IOException">Thrown when the blob is missing or unreadable.</exception>
    byte[] Read(string blobRef);

    /// <summary>Whether a blob is stored under <paramref name="blobRef"/>.</summary>
    /// <param name="blobRef">A reference returned by <see cref="Put"/>.</param>
    /// <returns><c>true</c> when the blob exists.</returns>
    /// <exception cref="System.ArgumentException">Thrown when <paramref name="blobRef"/> is not a well-formed reference.</exception>
    bool Exists(string blobRef);

    /// <summary>Delete the blob under <paramref name="blobRef"/> if present (used by retention pruning). Missing blobs are ignored.</summary>
    /// <param name="blobRef">A reference returned by <see cref="Put"/>.</param>
    /// <exception cref="System.ArgumentException">Thrown when <paramref name="blobRef"/> is not a well-formed reference.</exception>
    void Remove(string blobRef);
}