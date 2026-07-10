using System.Text;

namespace RaccoonNinja.McpToolset.Server.FileVault.Domain;

/// <summary>
/// The blake3 hash of a snapshot's bytes, stored as lowercase hex. Used as a version-token
/// integrity check and to make concurrent snapshot filenames collision-free. blake3 (not SHA-256)
/// keeps hash continuity with stores written by the Rust server.
/// </summary>
public sealed record ContentHash
{
    /// <summary>The number of hex characters embedded in snapshot filenames.</summary>
    public const int ShortLength = 12;

    private ContentHash(string hex)
    {
        Hex = hex;
    }

    /// <summary>The full lowercase-hex digest.</summary>
    public string Hex { get; }

    /// <summary>A short prefix suitable for embedding in a snapshot filename.</summary>
    public string ShortHex => Hex[..Math.Min(ShortLength, Hex.Length)];

    /// <summary>Compute the blake3 hash of <paramref name="bytes"/>.</summary>
    /// <param name="bytes">The content to hash.</param>
    /// <returns>The lowercase-hex digest wrapper.</returns>
    public static ContentHash Of(ReadOnlySpan<byte> bytes)
        => new(Blake3.Hasher.Hash(bytes).ToString());

    /// <summary>Compute the blake3 hash of the UTF-8 encoding of <paramref name="text"/>.</summary>
    /// <param name="text">The content to hash.</param>
    /// <returns>The lowercase-hex digest wrapper.</returns>
    public static ContentHash OfText(string text)
        => Of(Encoding.UTF8.GetBytes(text ?? string.Empty));

    /// <summary>Wrap an already-computed lowercase-hex digest (e.g. read back from the database).</summary>
    /// <param name="hex">The stored digest.</param>
    /// <returns>The digest wrapper.</returns>
    public static ContentHash FromHex(string hex)
        => new(hex ?? string.Empty);

    /// <inheritdoc />
    public override string ToString() => Hex;
}