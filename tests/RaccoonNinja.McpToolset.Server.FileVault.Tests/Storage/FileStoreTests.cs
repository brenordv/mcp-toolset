using System.Text;
using RaccoonNinja.McpToolset.Server.FileVault.Domain;
using RaccoonNinja.McpToolset.Server.FileVault.Storage;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tests.Storage;

/// <summary>Write/read round-trip, traversal-guard, and atomic-write tests for <see cref="FileStore"/>.</summary>
public sealed class FileStoreTests : IDisposable
{
    private static readonly string[] TraversalCorpus =
    [
        "../escape.txt",
        "a/../b.txt",
        "./a.txt",
        "a/./b.txt",
        "a//b.txt",
        "a/b/",
        "/absolute/escape.txt",
        "a\\b.txt",
        "a:b.txt",
        "C:/escape.txt",
        @"C:\evil\escape.txt",
    ];

    private readonly string _outerRoot;
    private readonly string _storeRoot;
    private readonly FileStore _store;

    public FileStoreTests()
    {
        _outerRoot = Path.Combine(Path.GetTempPath(), "filevault-tests", Guid.NewGuid().ToString("N"));
        _storeRoot = Path.Combine(_outerRoot, "store");
        Directory.CreateDirectory(_storeRoot);
        _store = new FileStore(_storeRoot);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_outerRoot, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup; anything left over lives under the OS temp directory.
        }
        catch (UnauthorizedAccessException)
        {
            // Same best-effort contract.
        }

        GC.SuppressFinalize(this);
    }

    public static TheoryData<string> TraversalCases()
    {
        var data = new TheoryData<string>();
        foreach (var relPath in TraversalCorpus)
        {
            data.Add(relPath);
        }

        return data;
    }

    [Fact]
    public void WriteSnapshot_ThenReadSnapshot_ReturnsIdenticalBytes()
    {
        // Arrange
        var bytes = "plain snapshot content\nsecond line\n"u8.ToArray();

        // Act
        var hash = _store.WriteSnapshot("proj/notes/v1-abc.txt", bytes);
        var roundTripped = _store.ReadSnapshot("proj/notes/v1-abc.txt");

        // Assert
        Assert.Equal(bytes, roundTripped);
        Assert.Equal(ContentHash.Of(bytes), hash);
    }

    [Fact]
    public void WriteSnapshot_MultiByteContent_SurvivesRoundTrip()
    {
        // Arrange
        var bytes = "raccoon 🦝 🎉 emoji plus CJK: 日本語のテキスト 中文内容 한국어"u8.ToArray();

        // Act
        _ = _store.WriteSnapshot("proj/multibyte/v1-def.txt", bytes);
        var roundTripped = _store.ReadSnapshot("proj/multibyte/v1-def.txt");

        // Assert
        Assert.Equal(bytes, roundTripped);
        var text = Encoding.UTF8.GetString(roundTripped);
        Assert.Contains("🦝", text);
        Assert.Contains("日本語", text);
    }

    [Fact]
    public void WriteSnapshot_AnyContent_ReturnsLowercaseHexBlake3Hash()
    {
        // Arrange
        var bytes = "hash me"u8.ToArray();

        // Act
        var hash = _store.WriteSnapshot("proj/hash/v1-aaa.txt", bytes);

        // Assert
        Assert.Matches("^[0-9a-f]{64}$", hash.Hex);
        Assert.Equal(ContentHash.Of(bytes), hash);
    }

    [Fact]
    public void WriteSnapshot_NestedRelPath_CreatesFileOnlyInsideStoreRoot()
    {
        // Arrange
        var bytes = "confined"u8.ToArray();

        // Act
        _ = _store.WriteSnapshot("a/b/c.txt", bytes);

        // Assert
        Assert.True(File.Exists(Path.Combine(_storeRoot, "a", "b", "c.txt")));
        var allFiles = Directory.GetFiles(_outerRoot, "*", SearchOption.AllDirectories);
        Assert.All(allFiles, file => Assert.StartsWith(_storeRoot + Path.DirectorySeparatorChar, file, StringComparison.Ordinal));
    }

    [Fact]
    public void WriteSnapshot_Success_LeavesNoTempFileBehind()
    {
        // Arrange
        var bytes = "atomic write"u8.ToArray();

        // Act
        _ = _store.WriteSnapshot("proj/atomic/v1-bbb.txt", bytes);

        // Assert
        var allFiles = Directory.GetFiles(_storeRoot, "*", SearchOption.AllDirectories);
        var onlyFile = Assert.Single(allFiles);
        Assert.EndsWith("v1-bbb.txt", onlyFile);
        Assert.Empty(Directory.GetFiles(_storeRoot, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void WriteSnapshot_TargetAlreadyExists_TreatsWriteAsSatisfied()
    {
        // Arrange
        var bytes = "identical bytes"u8.ToArray();
        var first = _store.WriteSnapshot("proj/dup/v1-ccc.txt", bytes);

        // Act
        var second = _store.WriteSnapshot("proj/dup/v1-ccc.txt", bytes);

        // Assert
        Assert.Equal(first, second);
        var directory = Path.Combine(_storeRoot, "proj", "dup");
        Assert.Single(Directory.GetFiles(directory));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public void WriteSnapshot_TargetExistsWithDifferentBytes_KeepsExistingContent()
    {
        // Arrange
        var original = "first writer wins"u8.ToArray();
        var contender = "second writer loses"u8.ToArray();
        _ = _store.WriteSnapshot("proj/race/v1-ddd.txt", original);

        // Act
        var returnedHash = _store.WriteSnapshot("proj/race/v1-ddd.txt", contender);

        // Assert
        Assert.Equal(original, _store.ReadSnapshot("proj/race/v1-ddd.txt"));
        Assert.Equal(ContentHash.Of(contender), returnedHash);
        Assert.Empty(Directory.GetFiles(Path.Combine(_storeRoot, "proj", "race"), "*.tmp"));
    }

    [Theory]
    [MemberData(nameof(TraversalCases))]
    public void WriteSnapshot_UnsafeRelPath_ThrowsIOException(string relPath)
    {
        // Arrange
        var bytes = "never written"u8.ToArray();

        // Act
        Action write = () => _store.WriteSnapshot(relPath, bytes);
        Action read = () => _store.ReadSnapshot(relPath);
        Action exists = () => _store.SnapshotExists(relPath);

        // Assert
        Assert.Contains("unsafe component", Assert.Throws<IOException>(write).Message);
        Assert.Contains("unsafe component", Assert.Throws<IOException>(read).Message);
        Assert.Contains("unsafe component", Assert.Throws<IOException>(exists).Message);
    }

    [Fact]
    public void WriteSnapshot_TraversalCorpus_CreatesNothingOutsideStoreRoot()
    {
        // Arrange
        var bytes = "never written"u8.ToArray();

        // Act
        foreach (var relPath in TraversalCorpus)
        {
            Assert.Throws<IOException>(() => { _ = _store.WriteSnapshot(relPath, bytes); });
        }

        // Assert
        var onlyEntry = Assert.Single(Directory.EnumerateFileSystemEntries(_outerRoot, "*", SearchOption.AllDirectories));
        Assert.Equal(_storeRoot, onlyEntry);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_storeRoot));
    }

    [Fact]
    public void WriteSnapshot_EmptyRelPath_ThrowsIOException()
    {
        // Arrange
        var bytes = "never written"u8.ToArray();

        // Act
        Action act = () => _store.WriteSnapshot(string.Empty, bytes);

        // Assert
        var exception = Assert.Throws<IOException>(act);
        Assert.Contains("empty", exception.Message);
    }

    [Fact]
    public void WriteSnapshot_NullBytes_ThrowsArgumentNullException()
    {
        // Act
        Action act = () => _store.WriteSnapshot("proj/null/v1-eee.txt", null);

        // Assert
        Assert.Throws<ArgumentNullException>(act);
    }

    [Fact]
    public void Constructor_NullOrWhitespaceRoot_Throws()
    {
        // Act
        Action nullRoot = () => _ = new FileStore(null);
        Action blankRoot = () => _ = new FileStore("   ");

        // Assert
        Assert.Throws<ArgumentNullException>(nullRoot);
        Assert.Throws<ArgumentException>(blankRoot);
    }

    [Fact]
    public void SnapshotExists_WrittenAndMissingPaths_ReportsPresence()
    {
        // Arrange
        _ = _store.WriteSnapshot("proj/present/v1-fff.txt", "present"u8.ToArray());

        // Act
        var present = _store.SnapshotExists("proj/present/v1-fff.txt");
        var missing = _store.SnapshotExists("proj/present/v2-000.txt");

        // Assert
        Assert.True(present);
        Assert.False(missing);
    }

    [Fact]
    public void ReadSnapshot_MissingSnapshot_ThrowsIOException()
    {
        // Act
        Action act = () => _store.ReadSnapshot("proj/missing/v1-999.txt");

        // Assert
        // ThrowsAny: the concrete type is a subclass (DirectoryNotFoundException) and the
        // contract under test is the IOException family.
        Assert.ThrowsAny<IOException>(act);
    }
}