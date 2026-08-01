using RaccoonNinja.McpToolset.Files.Security;
using RaccoonNinja.McpToolset.Files.Storage;
using RaccoonNinja.McpToolset.Server.TextEdit.Journal;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Content;

/// <summary>
/// Restores a journaled batch. Undo is a second write path, so it shares the same confinement and denylist
/// gate the forward path uses: every stored journal path is untrusted input, re-confined and re-denylisted
/// before anything is written, and a row that no longer confines or is now denylisted is skipped and named,
/// never restored. The hash gate (current content equals the recorded post-image) is a concurrency check
/// layered on top of that gate, not a substitute for it. A file deleted since the batch is recreated from
/// its pre-image, with any missing parent directory created only under the re-confined path.
/// </summary>
public sealed class Undoer(IRootResolver root, ISecretDenylist denylist, JournalStore journal, long maxBytes)
{
    private readonly IRootResolver _root = root ?? throw new ArgumentNullException(nameof(root));
    private readonly ISecretDenylist _denylist = denylist ?? throw new ArgumentNullException(nameof(denylist));
    private readonly JournalStore _journal = journal ?? throw new ArgumentNullException(nameof(journal));

    /// <summary>Undo <paramref name="batchId"/>, restoring each row that still confines, is not denylisted, and matches its post-image.</summary>
    /// <param name="batchId">The batch to undo (assumed to exist; the caller validates it first).</param>
    /// <returns>The undo outcome.</returns>
    public UndoOutcome Undo(long batchId)
    {
        var restored = new List<string>();
        var recreated = new List<string>();
        var skipped = new List<SkippedUndo>();

        foreach (var file in _journal.GetBatchFiles(batchId))
        {
            ConfinedPath confined;
            try
            {
                confined = _root.Confine(file.Path, "path");
            }
            catch (PathConfinementException)
            {
                skipped.Add(new SkippedUndo(file.Path, UndoSkipReason.OutOfRoot));
                continue;
            }

            if (_denylist.IsDeniedFile(confined.RelativePath))
            {
                skipped.Add(new SkippedUndo(confined.RelativePath, UndoSkipReason.Denied));
                continue;
            }

            byte[] preImage;
            try
            {
                preImage = _journal.ReadPreImage(file.BlobRef);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped.Add(new SkippedUndo(confined.RelativePath, UndoSkipReason.IoError));
                continue;
            }

            if (!confined.Exists)
            {
                Recreate(confined, preImage, recreated, skipped);
                continue;
            }

            if (string.IsNullOrEmpty(file.PostHash))
            {
                // A pending row: the batch crashed before finalizing this file, so the write may or may
                // not have landed. Restoring the pre-image is safe either way (a revert if it landed, a
                // no-op if it did not), so it is not hash-gated. Confinement and the denylist were already
                // re-checked above.
                RestorePreImage(confined, preImage, restored, skipped);
                continue;
            }

            RestoreExisting(confined, file.PostHash, preImage, restored, skipped);
        }

        return new UndoOutcome
        {
            BatchId = batchId,
            Restored = restored,
            Recreated = recreated,
            Skipped = skipped,
        };
    }

    private static void Recreate(ConfinedPath confined, byte[] preImage, List<string> recreated, List<SkippedUndo> skipped)
    {
        try
        {
            // The confined real path is inside the root even when its parent is missing (the confiner
            // appends the not-yet-created segments to the longest existing ancestor), so AtomicWriter
            // creates the parent under the re-confined path, never outside it.
            AtomicWriter.Replace(confined.RealPath, preImage);
            recreated.Add(confined.RelativePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            skipped.Add(new SkippedUndo(confined.RelativePath, UndoSkipReason.IoError));
        }
    }

    private static void RestorePreImage(ConfinedPath confined, byte[] preImage, List<string> restored, List<SkippedUndo> skipped)
    {
        try
        {
            AtomicWriter.ReplacePreservingMetadata(confined.RealPath, preImage);
            restored.Add(confined.RelativePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            skipped.Add(new SkippedUndo(confined.RelativePath, UndoSkipReason.IoError));
        }
    }

    private void RestoreExisting(ConfinedPath confined, string postHash, byte[] preImage, List<string> restored, List<SkippedUndo> skipped)
    {
        byte[] current;
        try
        {
            using var stream = new FileStream(confined.RealPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > maxBytes)
            {
                // A file larger than the write cap cannot equal a pre-image's post-image hash: it was changed.
                skipped.Add(new SkippedUndo(confined.RelativePath, UndoSkipReason.HashMismatch));
                return;
            }

            current = new byte[stream.Length];
            stream.ReadExactly(current, 0, current.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            skipped.Add(new SkippedUndo(confined.RelativePath, UndoSkipReason.IoError));
            return;
        }

        var currentHash = Blake3.Hasher.Hash(current).ToString();
        if (!string.Equals(currentHash, postHash, StringComparison.Ordinal))
        {
            skipped.Add(new SkippedUndo(confined.RelativePath, UndoSkipReason.HashMismatch));
            return;
        }

        try
        {
            AtomicWriter.ReplacePreservingMetadata(confined.RealPath, preImage);
            restored.Add(confined.RelativePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            skipped.Add(new SkippedUndo(confined.RelativePath, UndoSkipReason.IoError));
        }
    }
}