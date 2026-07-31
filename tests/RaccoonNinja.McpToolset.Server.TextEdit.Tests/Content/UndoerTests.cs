using RaccoonNinja.McpToolset.Server.TextEdit.Content;
using RaccoonNinja.McpToolset.Server.TextEdit.Journal;
using RaccoonNinja.McpToolset.Server.TextEdit.Tests.TestSupport;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Tests.Content;

public sealed class UndoerTests : IDisposable
{
    private readonly TextEditHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void Undo_ChangedFile_RestoresThePreImage()
    {
        // Arrange
        _harness.WriteText("a.txt", "hello world");
        var batch = _harness.Apply("replace_text", Replace("world", "text-edit"), "a.txt");

        // Act
        var outcome = _harness.Undoer.Undo(batch.BatchId.Value);

        // Assert
        Assert.Equal(["a.txt"], outcome.Restored);
        Assert.Equal("hello world", _harness.ReadText("a.txt"));
    }

    [Fact]
    public void Undo_FileChangedSinceBatch_IsSkippedAndNeverClobbered()
    {
        // Arrange
        _harness.WriteText("a.txt", "hello world");
        var batch = _harness.Apply("replace_text", Replace("world", "text-edit"), "a.txt");
        _harness.WriteText("a.txt", "a later manual edit");

        // Act
        var outcome = _harness.Undoer.Undo(batch.BatchId.Value);

        // Assert
        Assert.Contains(outcome.Skipped, skip => skip.Path == "a.txt" && skip.Reason == UndoSkipReason.HashMismatch);
        Assert.Equal("a later manual edit", _harness.ReadText("a.txt"));
    }

    [Fact]
    public void Undo_AfterWriteFailure_HashGatesInsteadOfClobberingLaterEdit()
    {
        // A write that fails after its row is journaled must finalize the row (post == pre) so undo
        // hash-gates it. A row left pending would be restored unconditionally and clobber an edit made
        // after the failure.
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Blocking the replace while still allowing the gate's read is Windows sharing semantics.");
        }

        // Arrange
        _harness.WriteText("a.txt", "hello world");
        var full = Path.Combine(_harness.Root, "a.txt");

        BatchOutcome outcome;
        using (File.Open(full, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            // FileShare.Read lets the gate read the file but denies the delete the atomic replace needs, so
            // the write fails only after BeginBatch has already committed the row.
            outcome = _harness.Apply("replace_text", Replace("world", "there"), "a.txt");
        }

        Assert.Equal(RefusalReason.WriteFailed, outcome.Files[0].Reason);
        Assert.NotNull(outcome.BatchId);
        Assert.Equal("hello world", _harness.ReadText("a.txt"));

        // A later manual edit changes the file out from under the journaled (post == pre) hash.
        _harness.WriteText("a.txt", "a later manual edit");

        // Act
        var undo = _harness.Undoer.Undo(outcome.BatchId.Value);

        // Assert: the hash gate skips it, so the manual edit survives.
        Assert.Contains(undo.Skipped, skip => skip.Path == "a.txt" && skip.Reason == UndoSkipReason.HashMismatch);
        Assert.Equal("a later manual edit", _harness.ReadText("a.txt"));
    }

    [Fact]
    public void Undo_DeletedFile_IsRecreatedFromPreImage()
    {
        // Arrange
        _harness.WriteText("nested/a.txt", "hello world");
        var batch = _harness.Apply("replace_text", Replace("world", "text-edit"), "nested/a.txt");
        _harness.Delete("nested/a.txt");

        // Act
        var outcome = _harness.Undoer.Undo(batch.BatchId.Value);

        // Assert
        Assert.Equal(["nested/a.txt"], outcome.Recreated);
        Assert.Equal("hello world", _harness.ReadText("nested/a.txt"));
    }

    [Fact]
    public void Undo_PoisonedJournalPathEscapingRoot_IsSkippedAndNeverWritten()
    {
        // Arrange
        var batchId = _harness.Journal.BeginBatch("replace_text", "root", "t", [Row("../evil.txt", "payload"u8.ToArray())]);
        var escaped = Path.Combine(_harness.Root, "..", "evil.txt");

        // Act
        var outcome = _harness.Undoer.Undo(batchId);

        // Assert
        Assert.Contains(outcome.Skipped, skip => skip.Reason == UndoSkipReason.OutOfRoot);
        Assert.False(File.Exists(escaped));
    }

    [Fact]
    public void Undo_PoisonedJournalPathNowDenylisted_IsSkipped()
    {
        // Arrange
        var batchId = _harness.Journal.BeginBatch("replace_text", "root", "t", [Row(".env", "payload"u8.ToArray())]);

        // Act
        var outcome = _harness.Undoer.Undo(batchId);

        // Assert
        Assert.Contains(outcome.Skipped, skip => skip.Path == ".env" && skip.Reason == UndoSkipReason.Denied);
        Assert.False(_harness.Exists(".env"));
    }

    [Fact]
    public void Undo_PendingRowAfterCrash_RestoresPreImageWithoutHashGate()
    {
        // Arrange
        _harness.WriteText("a.txt", "the content that landed before the crash");
        var batchId = _harness.Journal.BeginBatch(
            "replace_text",
            "root",
            "t",
            [Row("a.txt", "the original pre-image"u8.ToArray(), postHash: null)]);

        // Act
        var outcome = _harness.Undoer.Undo(batchId);

        // Assert
        Assert.Equal(["a.txt"], outcome.Restored);
        Assert.Equal("the original pre-image", _harness.ReadText("a.txt"));
    }

    private Replacer Replace(string pattern, string replacement)
        => new(pattern, replacement, isRegex: false, caseSensitive: false, _harness.Config);

    private FileRecord Row(string path, byte[] preImage, string postHash = null)
        => new()
        {
            Path = path,
            PreHash = Blake3.Hasher.Hash(preImage).ToString(),
            BlobRef = _harness.Journal.PutPreImage(preImage),
            PostHash = postHash,
            Encoding = "utf-8",
            EncodingConfidence = 1.0,
            LadderStep = "bom",
            SourceEncodingSupplied = false,
            Outcome = postHash is null ? JournalOutcome.Pending : JournalOutcome.Changed,
        };
}