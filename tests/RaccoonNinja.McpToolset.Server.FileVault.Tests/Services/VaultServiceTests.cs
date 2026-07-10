using System.Text;
using RaccoonNinja.McpToolset.Server.FileVault.Configuration;
using RaccoonNinja.McpToolset.Server.FileVault.Domain;
using RaccoonNinja.McpToolset.Server.FileVault.Errors;
using RaccoonNinja.McpToolset.Server.FileVault.Services;
using RaccoonNinja.McpToolset.Server.FileVault.Storage;
using RaccoonNinja.McpToolset.Server.FileVault.Tests.Storage;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tests.Services;

/// <summary>
/// Integration tests for <see cref="VaultService"/> over a real repository and file store:
/// the save/append/edit flows, conflict-hint enrichment and its degradation rules (including the
/// line-product guard), metadata caps, and the purge confirm gate.
/// </summary>
public sealed class VaultServiceTests : IDisposable
{
    private static readonly ProjectName Project = ProjectName.Parse("proj");
    private static readonly FileName Name = FileName.Parse("note-a");

    private readonly string _home;
    private readonly SqliteConnectionFactory _factory;
    private readonly FileStore _files;
    private readonly VaultConfig _config;
    private VaultService _service;

    public VaultServiceTests()
    {
        _home = Path.Combine(Path.GetTempPath(), "filevault-tests", Guid.NewGuid().ToString("N"));
        _config = SqliteVaultRepositoryTests.TestConfig(_home);
        Directory.CreateDirectory(_config.FilesDir);
        _factory = new SqliteConnectionFactory(_config);
        Migrator.Run(_factory);
        _files = new FileStore(_config.FilesDir);
        _service = new VaultService(new SqliteVaultRepository(_factory), _files, _config);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_home, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort temp cleanup.
        }
    }

    [Fact]
    public void Save_FirstSave_CommitsVersionOneAndWritesSnapshot()
    {
        // Act
        var committed = Save("hello vault", baseVersion: null);

        // Assert
        Assert.Equal(1, committed.Version);
        var (record, content, _) = _service.Get(Project, Name, version: null);
        Assert.Equal("hello vault", content);
        Assert.Equal($"proj/note-a/v0001-{committed.Hash.ShortHex}.txt", record.RelPath);
        Assert.True(_files.SnapshotExists(record.RelPath));
    }

    [Fact]
    public void Save_StaleBase_ThrowsConflictWithDiffHint()
    {
        // Arrange
        Save("line one\nline two", baseVersion: null);
        Save("line one\nline two CHANGED", baseVersion: 1);

        // Act: a second writer still at base_version 1.
        Action act = () => Save("line one\nsomething else", baseVersion: 1);

        // Assert: the hint carries base/current and a base-to-current diff.
        var conflict = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.Conflict, conflict.Code);
        Assert.Equal(2, conflict.CurrentVersion);
        Assert.Equal(1, conflict.BaseVersion);
        Assert.NotNull(conflict.Diff);
        Assert.Contains("-line two", conflict.Diff);
        Assert.Contains("+line two CHANGED", conflict.Diff);
    }

    [Fact]
    public void Save_FirstSaveRace_ThrowsHintlessConflict()
    {
        // Arrange: a duplicate first save has no base_version, so no hint is possible.
        Save("winner", baseVersion: null);

        // Act
        Action act = () => Save("loser", baseVersion: null);

        // Assert
        var conflict = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.Conflict, conflict.Code);
        Assert.Null(conflict.BaseVersion);
        Assert.Null(conflict.Diff);
    }

    [Fact]
    public void Save_ConflictWithUnreadableBaseSnapshot_DegradesToHintlessConflict()
    {
        // Arrange: destroy the base snapshot so hint enrichment cannot read it back.
        Save("v1 content", baseVersion: null);
        var v1RelPath = _service.Get(Project, Name, version: 1).Record.RelPath;
        Save("v2 content", baseVersion: 1);
        _files.RemoveSnapshots([v1RelPath]);

        // Act
        Action act = () => Save("stale write", baseVersion: 1);

        // Assert
        var conflict = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.Conflict, conflict.Code);
        Assert.Equal(2, conflict.CurrentVersion);
        Assert.Null(conflict.Diff);
    }

    [Fact]
    public void Save_ConflictAboveLineProductGuard_DegradesToHintlessConflict()
    {
        // Arrange
        var manyLines = string.Join('\n', Enumerable.Range(0, 2001).Select(i => "line " + i));
        Save(manyLines, baseVersion: null);
        Save(manyLines + "\nextra", baseVersion: 1);

        // Act
        Action act = () => Save("stale", baseVersion: 1);

        // Assert
        var conflict = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.Conflict, conflict.Code);
        Assert.Null(conflict.Diff);
    }

    [Fact]
    public void Save_ContentOverConfiguredLimit_ThrowsTooLarge()
    {
        // Arrange
        var smallLimit = SqliteVaultRepositoryTests.TestConfig(_home) with { MaxContentBytes = 8 };
        _service = new VaultService(new SqliteVaultRepository(_factory), _files, smallLimit);

        // Act
        Action act = () => Save("nine bytes", baseVersion: null);

        // Assert
        var ex = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.TooLarge, ex.Code);
    }

    [Fact]
    public void Save_SummaryOverSixteenKiB_ThrowsTooLarge()
    {
        // Act
        Action act = () => _service.Save(
            Project, Name, "content", new string('s', 16 * 1024 + 1), null, null, VaultFormat.Text, ParentUpdate.Leave);

        // Assert
        var ex = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.TooLarge, ex.Code);
    }

    [Fact]
    public void Save_TooManyTags_ThrowsTooLarge()
    {
        // Arrange
        var tags = Enumerable.Range(0, 65).Select(i => "tag" + i).ToList();

        // Act
        Action act = () => _service.Save(Project, Name, "content", "s", null, tags, VaultFormat.Text, ParentUpdate.Leave);

        // Assert
        var ex = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.TooLarge, ex.Code);
    }

    [Fact]
    public void Save_TagOver128Chars_ThrowsTooLarge()
    {
        // Act
        Action act = () => _service.Save(
            Project, Name, "content", "s", null, [new string('t', 129)], VaultFormat.Text, ParentUpdate.Leave);

        // Assert
        var ex = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.TooLarge, ex.Code);
    }

    [Fact]
    public void Append_ConcatenatesRawWithoutSeparator()
    {
        // Arrange
        Save("abc", baseVersion: null);

        // Act
        var committed = _service.Append(Project, Name, "def", baseVersion: 1);

        // Assert
        Assert.Equal(2, committed.Version);
        Assert.Equal("abcdef", _service.Get(Project, Name, version: null).Content);
        Assert.Equal(VaultOp.Append, _service.History(Project, Name)[0].Op);
    }

    [Fact]
    public void Append_KeepsSummaryAndTags()
    {
        // Arrange
        _service.Save(Project, Name, "abc", "keep this summary", null, ["keep-tag"], VaultFormat.Text, ParentUpdate.Leave);

        // Act
        _service.Append(Project, Name, "def", baseVersion: 1);

        // Assert
        var (record, _, _) = _service.Get(Project, Name, version: null);
        Assert.Equal("keep this summary", record.Summary);
        Assert.Equal(["keep-tag"], record.Tags);
    }

    [Fact]
    public void Append_EmptyContent_IsLegalAndCommitsNewVersion()
    {
        // Arrange
        Save("abc", baseVersion: null);

        // Act
        var committed = _service.Append(Project, Name, string.Empty, baseVersion: 1);

        // Assert
        Assert.Equal(2, committed.Version);
        Assert.Equal("abc", _service.Get(Project, Name, version: null).Content);
    }

    [Fact]
    public void Append_WrongBaseVersion_ConflictsAtReadTime()
    {
        // Arrange
        Save("only version", baseVersion: null);

        // Act
        Action act = () => _service.Append(Project, Name, "tail", baseVersion: 2);

        // Assert
        var conflict = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.Conflict, conflict.Code);
        Assert.Equal(1, conflict.CurrentVersion);
        Assert.Single(_service.History(Project, Name));
    }

    [Fact]
    public void Append_ArchivedFile_ThrowsArchived()
    {
        // Arrange
        Save("abc", baseVersion: null);
        _service.Archive(Project, Name);

        // Act
        Action act = () => _service.Append(Project, Name, "def", baseVersion: 1);

        // Assert
        var ex = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.Archived, ex.Code);
    }

    [Fact]
    public void EditSection_NonMarkdownFile_ThrowsInvalidFormat()
    {
        // Arrange
        Save("plain text", baseVersion: null);

        // Act
        Action act = () => _service.EditSection(Project, Name, "Title", "new body", baseVersion: 1);

        // Assert
        var ex = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.InvalidFormat, ex.Code);
    }

    [Fact]
    public void EditSection_MarkdownFile_ReplacesSectionAndRecordsOp()
    {
        // Arrange
        _service.Save(
            Project, Name, "# Doc\n\n## Target\nold body\n\n## Next\nkeep\n", "s", null, null,
            VaultFormat.Markdown, ParentUpdate.Leave);

        // Act
        _service.EditSection(Project, Name, "Target", "new body", baseVersion: 1);

        // Assert
        var content = _service.Get(Project, Name, version: null).Content;
        Assert.Contains("new body", content);
        Assert.DoesNotContain("old body", content);
        Assert.Contains("## Next\nkeep", content);
        Assert.Equal(VaultOp.EditSection, _service.History(Project, Name)[0].Op);
    }

    [Fact]
    public void EditKey_JsonFile_SetsValueAndRecordsOp()
    {
        // Arrange
        _service.Save(Project, Name, "{\n  \"a\": {\n    \"b\": 1\n  }\n}", "s", null, null, VaultFormat.Json, ParentUpdate.Leave);
        using var value = System.Text.Json.JsonDocument.Parse("42");

        // Act
        _service.EditKey(Project, Name, "a.b", value.RootElement, baseVersion: 1);

        // Assert
        Assert.Contains("\"b\": 42", _service.Get(Project, Name, version: null).Content);
        Assert.Equal(VaultOp.EditKey, _service.History(Project, Name)[0].Op);
    }

    [Fact]
    public void EditKey_TextFile_ThrowsInvalidFormat()
    {
        // Arrange
        Save("plain", baseVersion: null);
        using var value = System.Text.Json.JsonDocument.Parse("1");

        // Act
        Action act = () => _service.EditKey(Project, Name, "a", value.RootElement, baseVersion: 1);

        // Assert
        var ex = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.InvalidFormat, ex.Code);
    }

    [Fact]
    public void SetMeta_NoFieldsSupplied_ThrowsNothingToUpdate()
    {
        // Arrange
        Save("content", baseVersion: null);

        // Act
        Action act = () => _service.SetMeta(Project, Name, summary: null, tags: null, ParentUpdate.Leave, baseVersion: null);

        // Assert
        var ex = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.NothingToUpdate, ex.Code);
    }

    [Fact]
    public void Get_ExplicitCurrentVersion_ReturnsVersionCapturedSummary()
    {
        // Arrange
        _service.Save(Project, Name, "content", "captured", null, null, VaultFormat.Text, ParentUpdate.Leave);
        _service.SetMeta(Project, Name, "live", tags: null, ParentUpdate.Leave, baseVersion: null);

        // Act
        var current = _service.Get(Project, Name, version: null);
        var explicitVersion = _service.Get(Project, Name, version: 1);

        // Assert
        Assert.Equal("live", current.Record.Summary);
        Assert.Equal("captured", explicitVersion.Record.Summary);
    }

    [Fact]
    public void Get_ArchivedFile_StillReturnsContent()
    {
        // Arrange
        Save("still readable", baseVersion: null);
        _service.Archive(Project, Name);

        // Act
        var (record, content, _) = _service.Get(Project, Name, version: null);

        // Assert
        Assert.Equal(FileState.Archived, record.State);
        Assert.Equal("still readable", content);
    }

    [Fact]
    public void History_ArchivedFile_StillWorks()
    {
        // Arrange
        Save("archived history", baseVersion: null);
        _service.Archive(Project, Name);

        // Act
        var history = _service.History(Project, Name);

        // Assert
        var entry = Assert.Single(history);
        Assert.Equal(1, entry.Version);
    }

    [Fact]
    public void Purge_ArchivedFile_StillWorks()
    {
        // Arrange
        Save("archived then purged", baseVersion: null);
        _service.Archive(Project, Name);

        // Act
        _service.Purge(Project, Name, confirm: true);

        // Assert
        var ex = Assert.Throws<VaultException>(() => _service.Get(Project, Name, version: null));
        Assert.Equal(VaultErrorCode.NotFound, ex.Code);
    }

    [Fact]
    public void Purge_WithoutConfirm_ThrowsBeforeAnyDeletion()
    {
        // Arrange
        Save("precious", baseVersion: null);

        // Act
        Action act = () => _service.Purge(Project, Name, confirm: false);

        // Assert
        var ex = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.ConfirmationRequired, ex.Code);
        Assert.Equal("precious", _service.Get(Project, Name, version: null).Content);
    }

    [Fact]
    public void Purge_DefaultRetention_RenamesSnapshotsWithDeletedPrefix()
    {
        // Arrange
        var committed = Save("bytes to retain", baseVersion: null);
        var relPath = $"proj/note-a/v0001-{committed.Hash.ShortHex}.txt";

        // Act
        _service.Purge(Project, Name, confirm: true);

        // Assert
        Assert.False(_files.SnapshotExists(relPath));
        var retained = Path.Combine(_config.FilesDir, "proj", "note-a", $"DELETED_v0001-{committed.Hash.ShortHex}.txt");
        Assert.True(File.Exists(retained));
        Assert.Equal("bytes to retain", Encoding.UTF8.GetString(File.ReadAllBytes(retained)));
    }

    [Fact]
    public void Purge_DeleteFilesMode_RemovesSnapshotsOutright()
    {
        // Arrange
        _service = new VaultService(
            new SqliteVaultRepository(_factory), _files, _config with { PurgeDeleteFiles = true });
        var committed = Save("bytes to delete", baseVersion: null);
        var relPath = $"proj/note-a/v0001-{committed.Hash.ShortHex}.txt";

        // Act
        _service.Purge(Project, Name, confirm: true);

        // Assert
        Assert.False(_files.SnapshotExists(relPath));
        Assert.False(Directory.Exists(Path.Combine(_config.FilesDir, "proj", "note-a")));
    }

    [Fact]
    public void Save_ContentOverSplitThreshold_ReturnsSplitHintAndContentChars()
    {
        // Arrange
        UseSplitHintThreshold(10);

        // Act
        var committed = Save("12345678901", baseVersion: null);

        // Assert
        Assert.Equal(11, committed.ContentChars);
        Assert.Equal(
            "content is 11 chars; consider keeping this note as a summary + index "
            + "and moving detail into child notes linked via parent",
            committed.SplitHint);
    }

    [Fact]
    public void Save_ContentExactlyAtSplitThreshold_ReturnsNoSplitHint()
    {
        // Arrange
        UseSplitHintThreshold(10);

        // Act
        var committed = Save("1234567890", baseVersion: null);

        // Assert
        Assert.Equal(10, committed.ContentChars);
        Assert.Null(committed.SplitHint);
    }

    [Fact]
    public void Save_SplitHintDisabledViaZero_ReturnsNoSplitHintForLargeContent()
    {
        // Arrange
        // Act
        var committed = Save(new string('x', 50_000), baseVersion: null);

        // Assert
        Assert.Equal(50_000, committed.ContentChars);
        Assert.Null(committed.SplitHint);
    }

    [Fact]
    public void Save_AstralPlaneContent_CountsUtf16CodeUnits()
    {
        // Act
        var committed = Save("😀", baseVersion: null);

        // Assert
        Assert.Equal(2, committed.ContentChars);
    }

    [Fact]
    public void Append_SmallDeltaGrowsNotePastThreshold_ReturnsSplitHintForComposedContent()
    {
        // Arrange
        UseSplitHintThreshold(10);
        Save("123456", baseVersion: null);

        // Act
        var committed = _service.Append(Project, Name, "789012", baseVersion: 1);

        // Assert
        Assert.Equal(12, committed.ContentChars);
        Assert.NotNull(committed.SplitHint);
    }

    [Fact]
    public void EditSection_ComposedContentOverThreshold_ReturnsSplitHint()
    {
        // Arrange
        UseSplitHintThreshold(30);
        _service.Save(Project, Name, "# Target\nshort", "s", null, null, VaultFormat.Markdown, ParentUpdate.Leave);

        // Act
        var committed = _service.EditSection(Project, Name, "Target", new string('y', 40), baseVersion: 1);

        // Assert
        Assert.NotNull(committed.SplitHint);
    }

    [Fact]
    public void EditKey_ComposedContentOverThreshold_ReturnsSplitHint()
    {
        // Arrange
        UseSplitHintThreshold(20);
        _service.Save(Project, Name, "{\n  \"a\": 1\n}", "s", null, null, VaultFormat.Json, ParentUpdate.Leave);
        using var value = System.Text.Json.JsonDocument.Parse($"\"{new string('z', 30)}\"");

        // Act
        var committed = _service.EditKey(Project, Name, "a", value.RootElement, baseVersion: 1);

        // Assert
        Assert.NotNull(committed.SplitHint);
    }

    private void UseSplitHintThreshold(int chars)
        => _service = new VaultService(
            new SqliteVaultRepository(_factory), _files, _config with { SplitHintChars = chars });

    private Committed Save(string content, int? baseVersion)
        => _service.Save(Project, Name, content, "summary", baseVersion, null, VaultFormat.Text, ParentUpdate.Leave);
}