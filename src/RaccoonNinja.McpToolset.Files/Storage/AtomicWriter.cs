using System.Globalization;

namespace RaccoonNinja.McpToolset.Files.Storage;

/// <summary>
/// Writes bytes to a destination crash-safely: content goes to a temporary file in the destination's
/// own directory, is flushed to disk, then renamed into place. Same-directory placement is required
/// because a rename is only atomic within one volume, so the temp never lives in <c>%TEMP%</c> or
/// app-data. "Atomic" here means the rename is visible all-or-nothing; it is not a power-loss
/// durability guarantee (the BCL exposes no directory fsync without P/Invoke).
/// </summary>
public static class AtomicWriter
{
    private static long _tempCounter;

    /// <summary>
    /// Create the file at <paramref name="destination"/> with <paramref name="content"/> only if it
    /// does not already exist. A pre-existing target is left untouched and reported as
    /// <see cref="AtomicWriteOutcome.Skipped"/>, which is what content-addressed callers rely on for
    /// free deduplication.
    /// </summary>
    /// <param name="destination">The absolute destination path.</param>
    /// <param name="content">The bytes to write.</param>
    /// <returns><see cref="AtomicWriteOutcome.Written"/> when created, <see cref="AtomicWriteOutcome.Skipped"/> when the target already existed.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="destination"/> is null or blank.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="content"/> is null.</exception>
    /// <exception cref="IOException">Thrown when an I/O step fails.</exception>
    public static AtomicWriteOutcome WriteNew(string destination, byte[] content)
    {
        var temp = PrepareTemp(destination, content);
        try
        {
            File.Move(temp, destination);
            return AtomicWriteOutcome.Written;
        }
        catch (IOException) when (File.Exists(destination))
        {
            // A prior crash or a racing writer already placed the target; discard our temp.
            SafeDelete(temp);
            return AtomicWriteOutcome.Skipped;
        }
        catch
        {
            SafeDelete(temp);
            throw;
        }
    }

    /// <summary>
    /// Write <paramref name="content"/> to <paramref name="destination"/>, replacing any existing file.
    /// The temp-and-rename sequence keeps the destination either fully old or fully new; a reader never
    /// sees a partially written file.
    /// </summary>
    /// <param name="destination">The absolute destination path.</param>
    /// <param name="content">The bytes to write.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="destination"/> is null or blank.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="content"/> is null.</exception>
    /// <exception cref="IOException">Thrown when an I/O step fails.</exception>
    public static void Replace(string destination, byte[] content)
    {
        var temp = PrepareTemp(destination, content);
        try
        {
            File.Move(temp, destination, overwrite: true);
        }
        catch
        {
            SafeDelete(temp);
            throw;
        }
    }

    /// <summary>Create the destination directory, write the content to a uniquely named flushed temp file, and return its path.</summary>
    private static string PrepareTemp(string destination, byte[] content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(content);

        var directory = Path.GetDirectoryName(destination)
                        ?? throw new IOException($"destination '{destination}' has no parent directory");
        Directory.CreateDirectory(directory);

        var temp = TempPath(directory, destination);
        using var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(content);
        stream.Flush(flushToDisk: true);
        return temp;
    }

    /// <summary>Build a collision-free temp name in the destination directory (leading dot hides it on Unix).</summary>
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

    private static void SafeDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup; a stray temp under the target directory is harmless.
        }
        catch (UnauthorizedAccessException)
        {
            // Same best-effort contract.
        }
    }
}