using System.Globalization;
using RaccoonNinja.McpToolset.Server.FileVault.Domain;

namespace RaccoonNinja.McpToolset.Server.FileVault.Storage;

/// <summary>
/// The plain-text snapshot store. Each version is written once to an immutable file and never
/// reopened for writing. Writes are crash-safe: content goes to a temp file in the destination
/// directory, is flushed to disk, then atomically renamed into place, so the database (committed
/// after the rename) never references a snapshot that does not exist.
/// </summary>
public sealed class FileStore
{
    private static long _tempCounter;

    private readonly string _root;
    private readonly string _rootFullPath;

    /// <summary>Create a store rooted at <paramref name="root"/> (the configured files dir).</summary>
    /// <param name="root">The snapshot root directory.</param>
    public FileStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = root;
        _rootFullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    }

    /// <summary>
    /// Atomically write <paramref name="bytes"/> to the snapshot at <paramref name="relPath"/>
    /// and return their blake3 hash. If the destination already exists (a prior crash left the
    /// file, or an identical racing write won), the write is treated as satisfied: snapshot
    /// paths embed the content hash, so an existing target holds the same bytes.
    /// </summary>
    /// <param name="relPath">The <c>/</c>-separated path relative to the store root.</param>
    /// <param name="bytes">The snapshot content.</param>
    /// <returns>The content hash of <paramref name="bytes"/>.</returns>
    /// <exception cref="IOException">Thrown when an I/O step fails.</exception>
    public ContentHash WriteSnapshot(string relPath, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var destination = Resolve(relPath);
        var directory = Path.GetDirectoryName(destination)
                        ?? throw new IOException($"snapshot path '{relPath}' has no parent directory");
        Directory.CreateDirectory(directory);

        var temp = TempPath(directory, destination);
        using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        try
        {
            File.Move(temp, destination);
        }
        catch (IOException) when (File.Exists(destination))
        {
            // Same-hash target already present; the bytes on disk are identical by construction.
            File.Delete(temp);
        }

        return ContentHash.Of(bytes);
    }

    /// <summary>Returns <c>true</c> when the snapshot at <paramref name="relPath"/> already exists.</summary>
    /// <param name="relPath">The <c>/</c>-separated path relative to the store root.</param>
    /// <returns><c>true</c> when the target file is present.</returns>
    public bool SnapshotExists(string relPath)
        => File.Exists(Resolve(relPath));

    /// <summary>Read the bytes of the snapshot at <paramref name="relPath"/>.</summary>
    /// <param name="relPath">The <c>/</c>-separated path relative to the store root.</param>
    /// <returns>The snapshot content.</returns>
    /// <exception cref="IOException">Thrown when the snapshot is missing or unreadable.</exception>
    public byte[] ReadSnapshot(string relPath)
        => File.ReadAllBytes(Resolve(relPath));

    /// <summary>
    /// Remove the given snapshots (used only by purge). Missing files are ignored because a crash
    /// may have left an orphan, and purge is a best-effort cleanup. Empty parent directories are
    /// pruned.
    /// </summary>
    /// <param name="relPaths">The snapshot paths to remove.</param>
    /// <exception cref="IOException">Thrown only when a present file cannot be removed.</exception>
    public void RemoveSnapshots(IEnumerable<string> relPaths)
    {
        ArgumentNullException.ThrowIfNull(relPaths);
        foreach (var relPath in relPaths)
        {
            var path = Resolve(relPath);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            PruneEmptyDirectory(Path.GetDirectoryName(path));
        }
    }

    /// <summary>
    /// Retain the given snapshots instead of deleting them: rename each in place with a
    /// <c>DELETED_</c> prefix. The database record is gone, but the bytes stay on disk for manual
    /// recovery. Missing files are ignored. The rename is no-overwrite with a counter retry loop,
    /// so a concurrent or prior retained file can never be clobbered (TOCTOU-safe).
    /// </summary>
    /// <param name="relPaths">The snapshot paths to retain-rename.</param>
    /// <exception cref="IOException">Thrown only when a present file cannot be renamed.</exception>
    public void RetainSnapshots(IEnumerable<string> relPaths)
    {
        ArgumentNullException.ThrowIfNull(relPaths);
        foreach (var relPath in relPaths)
        {
            var path = Resolve(relPath);
            if (!File.Exists(path))
            {
                continue;
            }

            var fileName = Path.GetFileName(path);
            var directory = Path.GetDirectoryName(path) ?? _rootFullPath;
            MoveWithoutOverwrite(path, directory, fileName);
        }
    }

    /// <summary>
    /// Resolve a <c>/</c>-separated relative path against the store root in an OS-portable way.
    /// This is the defense-in-depth traversal guard layered on top of the validated name types:
    /// it rejects empty, <c>.</c>, <c>..</c>, and separator/drive components, then asserts the
    /// fully-resolved path stays inside the store root.
    /// </summary>
    private string Resolve(string relPath)
    {
        if (string.IsNullOrWhiteSpace(relPath))
        {
            throw new IOException("snapshot path is empty");
        }

        var path = _root;
        foreach (var segment in relPath.Split('/'))
        {
            if (segment.Length == 0 || segment == "." || segment == ".."
                || segment.Contains('\\') || segment.Contains(':'))
            {
                throw new IOException($"snapshot path '{relPath}' contains an unsafe component");
            }

            path = Path.Combine(path, segment);
        }

        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(_rootFullPath + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? fullPath
            : throw new IOException($"snapshot path '{relPath}' escapes the store root");
    }

    private static void MoveWithoutOverwrite(string source, string directory, string fileName)
    {
        var attempt = 0;
        while (true)
        {
            var candidateName = attempt == 0
                ? $"DELETED_{fileName}"
                : string.Create(CultureInfo.InvariantCulture, $"DELETED_{attempt}_{fileName}");
            var candidate = Path.Combine(directory, candidateName);
            try
            {
                File.Move(source, candidate);
                return;
            }
            catch (IOException) when (File.Exists(candidate))
            {
                attempt++;
            }
        }
    }

    private static void PruneEmptyDirectory(string directory)
    {
        if (directory is null || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            Directory.Delete(directory, recursive: false);
        }
        catch (IOException)
        {
            // Best-effort: only succeeds when the directory is empty.
        }
        catch (UnauthorizedAccessException)
        {
            // Same best-effort contract.
        }
    }

    private static string TempPath(string directory, string destination)
    {
        var stamp = DateTime.UtcNow.Ticks;
        var counter = Interlocked.Increment(ref _tempCounter);
        var baseName = Path.GetFileName(destination);
        return Path.Combine(
            directory,
            string.Create(
                CultureInfo.InvariantCulture,
                $".{baseName}.{Environment.ProcessId}.{stamp}.{counter}.tmp"));
    }
}