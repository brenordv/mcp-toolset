using RaccoonNinja.McpToolset.Files.Security;
using RaccoonNinja.McpToolset.Server.TextEdit.Journal;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Tests.Journal;

public sealed class JournalStoreTests : IDisposable
{
    private readonly string _root;
    private readonly string _appData;
    private readonly JournalStore _store;

    public JournalStoreTests()
    {
        _root = NewTempDir("rnmcp-te-root-");
        _appData = NewTempDir("rnmcp-te-appdata-");
        var paths = JournalPaths.Resolve(new RootConfinement(_root), _appData);
        _store = new JournalStore(paths);
        _store.EnsureSchema();
    }

    public void Dispose()
    {
        SafeDelete(_root);
        SafeDelete(_appData);
    }

    [Fact]
    public void BeginBatch_ThenGetBatch_ReturnsBatchAndPendingRows()
    {
        // Arrange
        var pending = Pending("src/a.txt", "before"u8.ToArray());

        // Act
        var batchId = _store.BeginBatch("replace_text", "root", "pattern_len=3", [pending]);

        // Assert
        var batch = _store.GetBatch(batchId);
        var files = _store.GetBatchFiles(batchId);
        Assert.NotNull(batch);
        Assert.Equal("replace_text", batch.Tool);
        Assert.Equal("pattern_len=3", batch.ArgsSummary);
        Assert.Single(files);
        Assert.Equal("src/a.txt", files[0].Path);
        Assert.Equal(JournalOutcome.Pending, files[0].Outcome);
        Assert.Null(files[0].PostHash);
    }

    [Fact]
    public void FinalizeChanged_FlipsRowsToChangedWithPostHash()
    {
        // Arrange
        var pending = Pending("a.txt", "before"u8.ToArray());
        var batchId = _store.BeginBatch("replace_text", "root", "x", [pending]);
        const string postHash = "deadbeef";

        // Act
        _store.FinalizeChanged(batchId, new Dictionary<string, string> { ["a.txt"] = postHash });

        // Assert
        var file = _store.GetBatchFiles(batchId)[0];
        Assert.Equal(JournalOutcome.Changed, file.Outcome);
        Assert.Equal(postHash, file.PostHash);
    }

    [Fact]
    public void PendingRowAfterCrash_KeepsPreImageRestorable()
    {
        // Arrange
        var preImage = "original"u8.ToArray();
        var pending = Pending("a.txt", preImage);

        // Act
        var batchId = _store.BeginBatch("replace_text", "root", "x", [pending]);

        // Assert
        var file = _store.GetBatchFiles(batchId)[0];
        Assert.Equal(JournalOutcome.Pending, file.Outcome);
        Assert.Equal(preImage, _store.ReadPreImage(file.BlobRef));
    }

    [Fact]
    public void ListRecent_ReturnsNewestFirst_AndLatestBatchIdMatches()
    {
        // Arrange
        var first = _store.BeginBatch("normalize_files", "root", "x", [Pending("a.txt", "a"u8.ToArray())]);
        var second = _store.BeginBatch("replace_text", "root", "y", [Pending("b.txt", "b"u8.ToArray())]);

        // Act
        var recent = _store.ListRecent(10);
        var latest = _store.LatestBatchId();

        // Assert
        Assert.Equal([second, first], recent.Select(batch => batch.BatchId));
        Assert.Equal(second, latest);
    }

    [Fact]
    public void GetBatch_UnknownId_ReturnsNull()
    {
        // Arrange
        // Act
        var batch = _store.GetBatch(9999);

        // Assert
        Assert.Null(batch);
    }

    [Fact]
    public void PutPreImage_ReadPreImage_RoundTrips()
    {
        // Arrange
        var content = "pre-image bytes"u8.ToArray();

        // Act
        var blobRef = _store.PutPreImage(content);

        // Assert
        Assert.Equal(content, _store.ReadPreImage(blobRef));
    }

    [Fact]
    public void PruneRetention_ByCount_DropsOldestAndKeepsNewest()
    {
        // Arrange
        var oldest = _store.BeginBatch("replace_text", "root", "x", [Pending("a.txt", "a"u8.ToArray())]);
        var newest = _store.BeginBatch("replace_text", "root", "y", [Pending("b.txt", "b"u8.ToArray())]);

        // Act
        var pruned = _store.PruneRetention(retentionBatches: 1, retentionHours: 48);

        // Assert
        Assert.Equal(1, pruned);
        Assert.Null(_store.GetBatch(oldest));
        Assert.NotNull(_store.GetBatch(newest));
    }

    [Fact]
    public void PruneRetention_SharedPreImage_SurvivesWhileOrphanIsRemoved()
    {
        // Arrange
        var shared = "shared content"u8.ToArray();
        var orphanOnly = "orphan content"u8.ToArray();
        var oldest = _store.BeginBatch("replace_text", "root", "x", [Pending("shared.txt", shared), Pending("orphan.txt", orphanOnly)]);
        _store.BeginBatch("replace_text", "root", "y", [Pending("also-shared.txt", shared)]);
        var sharedRef = _store.GetBatchFiles(oldest).Single(file => file.Path == "shared.txt").BlobRef;
        var orphanRef = _store.GetBatchFiles(oldest).Single(file => file.Path == "orphan.txt").BlobRef;

        // Act
        _store.PruneRetention(retentionBatches: 1, retentionHours: 48);

        // Assert
        Assert.Equal(shared, _store.ReadPreImage(sharedRef));
        Assert.Throws<FileNotFoundException>(() => _store.ReadPreImage(orphanRef));
    }

    [Fact]
    public void PruneRetention_ByAge_DropsEverythingOlderThanCutoff()
    {
        // Arrange
        _store.BeginBatch("replace_text", "root", "x", [Pending("a.txt", "a"u8.ToArray())]);
        _store.BeginBatch("replace_text", "root", "y", [Pending("b.txt", "b"u8.ToArray())]);

        // Act
        var pruned = _store.PruneRetention(retentionBatches: 100, retentionHours: 0);

        // Assert
        Assert.Equal(2, pruned);
        Assert.Empty(_store.ListRecent(10));
    }

    private FileRecord Pending(string path, byte[] preImage)
        => new()
        {
            Path = path,
            PreHash = Blake3.Hasher.Hash(preImage).ToString(),
            BlobRef = _store.PutPreImage(preImage),
            Encoding = "utf-8",
            EncodingConfidence = 1.0,
            LadderStep = "bom",
            SourceEncodingSupplied = false,
            Outcome = JournalOutcome.Pending,
        };

    private static string NewTempDir(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void SafeDelete(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
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
}