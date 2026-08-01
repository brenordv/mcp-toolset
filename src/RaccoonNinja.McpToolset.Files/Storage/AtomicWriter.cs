using System.Globalization;
using System.Security.AccessControl;

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

    /// <summary>
    /// Replace <paramref name="destination"/> like <see cref="Replace"/>, but first copy the existing
    /// file's access metadata onto the temp so the rewritten file keeps the source's permissions instead
    /// of inheriting the temp's defaults. On Unix the Unix file mode is copied with the setuid, setgid,
    /// and sticky bits stripped (the new content is caller-supplied, so an elevated bit must not carry
    /// over); on Windows the discretionary ACL is copied. The copy is best-effort: a metadata failure is
    /// reported by a <c>false</c> return but never aborts the write, because a permission-copy failure
    /// must not strand the content change. <see cref="Replace"/> keeps its metadata-agnostic behavior so
    /// content-addressed callers are unaffected.
    /// </summary>
    /// <param name="destination">The absolute destination path; when it exists, its metadata is preserved.</param>
    /// <param name="content">The bytes to write.</param>
    /// <returns><c>true</c> when metadata was preserved or there was nothing to preserve; <c>false</c> when the copy failed.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="destination"/> is null or blank.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="content"/> is null.</exception>
    /// <exception cref="IOException">Thrown when the write or rename fails.</exception>
    public static bool ReplacePreservingMetadata(string destination, byte[] content)
    {
        var temp = PrepareTemp(destination, content);
        try
        {
            var preserved = CopyAccessMetadata(destination, temp);
            File.Move(temp, destination, overwrite: true);
            return preserved;
        }
        catch
        {
            SafeDelete(temp);
            throw;
        }
    }

    /// <summary>
    /// Copy the access metadata of <paramref name="source"/> onto <paramref name="temp"/> before the temp
    /// is renamed into place. Fully self-contained: every failure mode of the platform metadata APIs is
    /// swallowed and reported as <c>false</c> so this can never propagate out and abort the pending write.
    /// </summary>
    private static bool CopyAccessMetadata(string source, string temp)
    {
        try
        {
            if (!File.Exists(source))
            {
                return true;
            }

            if (OperatingSystem.IsWindows())
            {
                // Round-trip the DACL through SDDL rather than passing the source's FileSecurity object
                // straight to SetAccessControl: a descriptor read via GetAccessControl has its
                // access-rules-modified flag unset, so SetAccessControl would persist nothing. Building a
                // fresh descriptor from the SDDL marks the DACL section dirty so it actually writes.
                var sourceAcl = new FileInfo(source).GetAccessControl(AccessControlSections.Access);
                var sddl = sourceAcl.GetSecurityDescriptorSddlForm(AccessControlSections.Access);
                var targetAcl = new FileSecurity();
                targetAcl.SetSecurityDescriptorSddlForm(sddl, AccessControlSections.Access);
                new FileInfo(temp).SetAccessControl(targetAcl);
            }
            else
            {
                const UnixFileMode elevated = UnixFileMode.SetUser | UnixFileMode.SetGroup | UnixFileMode.StickyBit;
                var mode = File.GetUnixFileMode(source) & ~elevated;
                File.SetUnixFileMode(temp, mode);
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // Covers PrivilegeNotHeldException from the Windows ACL path, which derives from it.
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            // A malformed SDDL round-trip (SetSecurityDescriptorSddlForm) surfaces here; the copy is
            // best-effort, so report failure rather than let it abort the pending write.
            return false;
        }
        catch (System.Security.SecurityException)
        {
            return false;
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