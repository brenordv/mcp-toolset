using RaccoonNinja.McpToolset.Server.FileVault.Domain;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tests.Domain;

/// <summary>Covers <see cref="ContentHash"/>: blake3 known vectors, hex shape, and round-trips.</summary>
public class ContentHashTests
{
    // Reference digests produced by the Rust vault-mcp binary (the official blake3 crate) while
    // generating Fixtures/rust-store; the store-compat contract is agreement with that
    // implementation, and these pin it at the unit level.
    private const string AlphaLineHex = "ac678d92b3d739773d18cd952cfcea443fa4a5a98ffc9554b66795bb22d5532d";

    private const string UnicodeNoteHex = "b0f52954fb8a6e2229d171c40cd9873b54cedb7e1abdc0ba7f1d5c8e9ac1240e";

    [Fact]
    public void OfText_AlphaLine_MatchesRustVaultMcpDigest()
    {
        // Act
        var hash = ContentHash.OfText("alpha\n");

        // Assert
        Assert.Equal(AlphaLineHex, hash.Hex);
        Assert.Equal("ac678d92b3d7", hash.ShortHex);
    }

    [Fact]
    public void OfText_MultiByteContent_MatchesRustVaultMcpDigest()
    {
        // Act
        var hash = ContentHash.OfText("emoji 🦝 and 中文テキスト\n");

        // Assert
        Assert.Equal(UnicodeNoteHex, hash.Hex);
        Assert.Equal("b0f52954fb8a", hash.ShortHex);
    }

    [Fact]
    public void OfText_EmptyString_EqualsHashOfEmptyBytes()
    {
        // Act
        var fromText = ContentHash.OfText(string.Empty);
        var fromBytes = ContentHash.Of(ReadOnlySpan<byte>.Empty);

        // Assert
        Assert.Equal(fromBytes, fromText);
        Assert.Matches("^[0-9a-f]{64}$", fromText.Hex);
    }

    [Fact]
    public void OfText_AnyInput_ProducesLowercaseHexOf64Chars()
    {
        // Act
        var hash = ContentHash.OfText("hello world");

        // Assert
        Assert.Matches("^[0-9a-f]{64}$", hash.Hex);
    }

    [Fact]
    public void OfText_Null_IsTreatedAsEmptyString()
    {
        // Act
        var hash = ContentHash.OfText(null);

        // Assert
        Assert.Equal(ContentHash.OfText(string.Empty), hash);
    }

    [Fact]
    public void FromHex_StoredDigest_RoundTrips()
    {
        // Act
        var hash = ContentHash.FromHex(AlphaLineHex);

        // Assert
        Assert.Equal(AlphaLineHex, hash.Hex);
    }

    [Fact]
    public void FromHex_Null_YieldsEmptyHex()
    {
        // Act
        var hash = ContentHash.FromHex(null);

        // Assert
        Assert.Empty(hash.Hex);
        Assert.Empty(hash.ShortHex);
    }

    [Fact]
    public void ShortHex_DigestShorterThanShortLength_ReturnsWholeDigest()
    {
        // Arrange
        var hash = ContentHash.FromHex("abc123");

        // Act
        var shortHex = hash.ShortHex;

        // Assert
        Assert.Equal("abc123", shortHex);
    }

    [Fact]
    public void ToString_Always_ReturnsHex()
    {
        // Arrange
        var hash = ContentHash.OfText("content");

        // Act
        var text = hash.ToString();

        // Assert
        Assert.Equal(hash.Hex, text);
    }

    [Fact]
    public void OfText_SameInput_ProducesEqualRecords()
    {
        // Act
        var first = ContentHash.OfText("same content");
        var second = ContentHash.OfText("same content");

        // Assert
        Assert.Equal(second, first);
    }

    [Fact]
    public void ShortLength_MatchesSnapshotFilenameContract()
    {
        // Assert
        Assert.Equal(12, ContentHash.ShortLength);
    }
}