using System.Text;
using RaccoonNinja.McpToolset.Files.Storage;

namespace RaccoonNinja.McpToolset.Files.Tests.Storage;

public sealed class BlobStoreTests : IDisposable
{
    private readonly string _root;
    private readonly BlobStore _store;

    public BlobStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "rnmcp-blob-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _store = new BlobStore(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public void Put_ThenRead_RoundTripsBytes()
    {
        // Arrange
        var content = "raccoon 🦝 snapshot 日本語"u8.ToArray();

        // Act
        var reference = _store.Put(content);
        var roundTripped = _store.Read(reference);

        // Assert
        Assert.Equal(content, roundTripped);
    }

    [Fact]
    public void Put_ReturnsLowercase64CharHexReference()
    {
        // Arrange
        // Act
        var reference = _store.Put("hash me"u8.ToArray());

        // Assert
        Assert.Matches("^[0-9a-f]{64}$", reference);
    }

    [Fact]
    public void Put_IdenticalContentTwice_DeduplicatesToOneBlob()
    {
        // Arrange
        var content = "identical bytes"u8.ToArray();

        // Act
        var first = _store.Put(content);
        var second = _store.Put(content);

        // Assert - same reference, and only one blob file on disk.
        Assert.Equal(first, second);
        Assert.Single(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void Put_DifferentContent_ProducesDifferentReferences()
    {
        // Arrange
        // Act
        var a = _store.Put("alpha"u8.ToArray());
        var b = _store.Put("beta"u8.ToArray());

        // Assert
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Put_ShardsBlobUnderFirstTwoHexCharacters()
    {
        // Arrange
        // Act
        var reference = _store.Put("shard me"u8.ToArray());

        // Assert
        var expected = Path.Combine(_root, reference[..2], reference);
        Assert.True(File.Exists(expected));
    }

    [Fact]
    public void Exists_PresentAndAbsentReferences_ReportsAccurately()
    {
        // Arrange
        var reference = _store.Put("present"u8.ToArray());
        var absent = new string('a', 64);

        // Act
        // Assert
        Assert.True(_store.Exists(reference));
        Assert.False(_store.Exists(absent));
    }

    [Fact]
    public void Remove_ExistingBlob_DeletesIt()
    {
        // Arrange
        var reference = _store.Put("delete me"u8.ToArray());

        // Act
        _store.Remove(reference);

        // Assert
        Assert.False(_store.Exists(reference));
    }

    [Fact]
    public void Remove_MissingBlob_DoesNotThrow()
    {
        // Arrange
        var absent = new string('b', 64);

        // Act
        var exception = Record.Exception(() => _store.Remove(absent));

        // Assert
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-hex")]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")]
    [InlineData("../../etc/passwd")]
    [InlineData("deadbeef")]
    public void Read_MalformedReference_ThrowsArgumentException(string blobRef)
    {
        // Arrange
        // Act
        // Assert
        Assert.Throws<ArgumentException>(() => _store.Read(blobRef));
    }

    [Fact]
    public void Read_ValidButMissingReference_ThrowsIOException()
    {
        // Arrange
        var missing = new string('c', 64);

        // Act
        // Assert
        Assert.ThrowsAny<IOException>(() => _store.Read(missing));
    }

    [Fact]
    public void Put_NullContent_Throws()
    {
        // Arrange
        // Act
        // Assert
        Assert.Throws<ArgumentNullException>(() => _store.Put(null));
    }

    [Fact]
    public void Constructor_BlankRoot_Throws()
    {
        // Arrange
        // Act
        // Assert
        Assert.Throws<ArgumentException>(() => new BlobStore("   "));
    }

    [Fact]
    public void Read_MatchesFileVaultContentHash()
    {
        // Arrange - the blob reference must equal the BLAKE3 hex the vault store already computes.
        var content = Encoding.UTF8.GetBytes("cross-check with ContentHash");
        var expected = Blake3.Hasher.Hash(content).ToString();

        // Act
        var reference = _store.Put(content);

        // Assert
        Assert.Equal(expected, reference);
    }
}