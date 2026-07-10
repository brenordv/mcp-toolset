using System.Text;
using RaccoonNinja.McpToolset.Server.FileVault.Storage;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tests.Storage;

/// <summary>
/// Purge-time snapshot disposal tests for <see cref="FileStore"/>: <c>RemoveSnapshots</c>
/// (the delete-files=true path, with parent-dir pruning) and <c>RetainSnapshots</c>
/// (the default DELETED_-prefix retention rename).
/// </summary>
public sealed class FileStoreRetentionTests : IDisposable
{
    private readonly string _outerRoot;
    private readonly string _storeRoot;
    private readonly FileStore _store;

    public FileStoreRetentionTests()
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

    [Fact]
    public void RemoveSnapshots_MissingSnapshot_DoesNotThrow()
    {
        // Arrange
        var paths = new[] { "proj/missing/v1-000.txt" };

        // Act
        var exception = Record.Exception(() => _store.RemoveSnapshots(paths));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void RemoveSnapshots_ExistingSnapshot_DeletesFileAndPrunesEmptyParent()
    {
        // Arrange
        _ = _store.WriteSnapshot("proj/doomed/v1-aaa.txt", "delete me"u8.ToArray());
        var paths = new[] { "proj/doomed/v1-aaa.txt" };

        // Act
        _store.RemoveSnapshots(paths);

        // Assert
        Assert.False(File.Exists(Path.Combine(_storeRoot, "proj", "doomed", "v1-aaa.txt")));
        Assert.False(Directory.Exists(Path.Combine(_storeRoot, "proj", "doomed")));
        // Pruning is best-effort and only removes the immediate parent.
        Assert.True(Directory.Exists(Path.Combine(_storeRoot, "proj")));
    }

    [Fact]
    public void RemoveSnapshots_ParentHoldsSibling_KeepsDirectoryAndSibling()
    {
        // Arrange
        var siblingBytes = Encoding.UTF8.GetBytes("survivor");
        _ = _store.WriteSnapshot("proj/shared/v1-aaa.txt", Encoding.UTF8.GetBytes("delete me"));
        _ = _store.WriteSnapshot("proj/shared/v2-bbb.txt", siblingBytes);
        var paths = new[] { "proj/shared/v1-aaa.txt" };

        // Act
        _store.RemoveSnapshots(paths);

        // Assert
        Assert.False(File.Exists(Path.Combine(_storeRoot, "proj", "shared", "v1-aaa.txt")));
        Assert.True(Directory.Exists(Path.Combine(_storeRoot, "proj", "shared")));
        Assert.Equal(siblingBytes, _store.ReadSnapshot("proj/shared/v2-bbb.txt"));
    }

    [Fact]
    public void RetainSnapshots_MissingSnapshot_DoesNotThrow()
    {
        // Arrange
        var paths = new[] { "proj/missing/v1-000.txt" };

        // Act
        var exception = Record.Exception(() => _store.RetainSnapshots(paths));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void RetainSnapshots_ExistingSnapshot_RenamesWithDeletedPrefix()
    {
        // Arrange
        var bytes = "keep me on disk"u8.ToArray();
        _ = _store.WriteSnapshot("proj/one.txt", bytes);
        var paths = new[] { "proj/one.txt" };

        // Act
        _store.RetainSnapshots(paths);

        // Assert
        Assert.False(File.Exists(Path.Combine(_storeRoot, "proj", "one.txt")));
        var retained = Path.Combine(_storeRoot, "proj", "DELETED_one.txt");
        Assert.True(File.Exists(retained));
        Assert.Equal(bytes, File.ReadAllBytes(retained));
    }

    [Fact]
    public void RetainSnapshots_DeletedTargetAlreadyExists_UsesCounterVariant()
    {
        // Arrange
        var earlierBytes = "earlier retained content"u8.ToArray();
        var bytes = "newly retained content"u8.ToArray();
        _ = _store.WriteSnapshot("proj/one.txt", bytes);
        var collision = Path.Combine(_storeRoot, "proj", "DELETED_one.txt");
        File.WriteAllBytes(collision, earlierBytes);
        var paths = new[] { "proj/one.txt" };

        // Act
        _store.RetainSnapshots(paths);

        // Assert
        var counterVariant = Path.Combine(_storeRoot, "proj", "DELETED_1_one.txt");
        Assert.True(File.Exists(counterVariant));
        Assert.Equal(bytes, File.ReadAllBytes(counterVariant));
        // A prior retained file must never be clobbered.
        Assert.Equal(earlierBytes, File.ReadAllBytes(collision));
        Assert.False(File.Exists(Path.Combine(_storeRoot, "proj", "one.txt")));
    }

    [Fact]
    public void RetainSnapshots_MultipleDeletedTargetsExist_IncrementsCounter()
    {
        // Arrange
        var bytes = "third retention"u8.ToArray();
        _ = _store.WriteSnapshot("proj/one.txt", bytes);
        File.WriteAllBytes(Path.Combine(_storeRoot, "proj", "DELETED_one.txt"), "first"u8.ToArray());
        File.WriteAllBytes(Path.Combine(_storeRoot, "proj", "DELETED_1_one.txt"), "second"u8.ToArray());
        var paths = new[] { "proj/one.txt" };

        // Act
        _store.RetainSnapshots(paths);

        // Assert
        var counterVariant = Path.Combine(_storeRoot, "proj", "DELETED_2_one.txt");
        Assert.True(File.Exists(counterVariant));
        Assert.Equal(bytes, File.ReadAllBytes(counterVariant));
    }
}