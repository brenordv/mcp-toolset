using Dapper;
using RaccoonNinja.McpToolset.Server.FileVault.Configuration;
using RaccoonNinja.McpToolset.Server.FileVault.Domain;
using RaccoonNinja.McpToolset.Server.FileVault.Errors;
using RaccoonNinja.McpToolset.Server.FileVault.Storage;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tests.Storage;

/// <summary>
/// Integration tests for <see cref="SqliteVaultRepository"/> against a real SQLite store in a
/// per-test temp directory: OCC semantics, the archived-before-conflict ordering, set_meta,
/// hierarchy rules, FTS-backed listing, and the purge shared-snapshot guard.
/// </summary>
public sealed class SqliteVaultRepositoryTests : IDisposable
{
    private readonly string _home;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteVaultRepository _repository;

    public SqliteVaultRepositoryTests()
    {
        _home = Path.Combine(Path.GetTempPath(), "filevault-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_home);
        var config = TestConfig(_home);
        _factory = new SqliteConnectionFactory(config);
        Migrator.Run(_factory);
        _repository = new SqliteVaultRepository(_factory);
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
            // Best-effort temp cleanup; the OS temp sweeper owns the rest.
        }
    }

    [Fact]
    public void CreateFirst_NewFile_CommitsVersionOne()
    {
        // Arrange
        var file = NewFile("proj", "note-a", "first content");

        // Act
        var committed = _repository.CreateFirst(file);

        // Assert
        Assert.Equal(1, committed.Version);
        Assert.Equal(file.Hash.Hex, committed.Hash.Hex);
        var record = _repository.GetCurrent("proj", "note-a");
        Assert.Equal(1, record.Version);
        Assert.Equal(FileState.Active, record.State);
        Assert.Null(record.Parent);
    }

    [Fact]
    public void CreateFirst_DuplicateName_ThrowsHintlessConflictWithWinnersVersion()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "note-a", "winner"));

        // Act
        var act = () => _repository.CreateFirst(NewFile("proj", "note-a", "loser"));

        // Assert
        var conflict = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.Conflict, conflict.Code);
        Assert.Equal(1, conflict.CurrentVersion);
        Assert.Null(conflict.BaseVersion);
        Assert.Null(conflict.Diff);
    }

    [Fact]
    public void CreateFirst_SameNameDifferentProject_BothSucceed()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj-a", "note", "a"));

        // Act
        var committed = _repository.CreateFirst(NewFile("proj-b", "note", "b"));

        // Assert
        Assert.Equal(1, committed.Version);
    }

    [Fact]
    public void CreateFirst_WithExistingParent_LinksChild()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "parent-note", "root"));

        // Act
        _repository.CreateFirst(NewFile("proj", "child-note", "leaf") with { Parent = "parent-note" });

        // Assert
        Assert.Equal("parent-note", _repository.GetCurrent("proj", "child-note").Parent);
        var children = _repository.Children("proj", "parent-note");
        var child = Assert.Single(children);
        Assert.Equal("child-note", child.Name);
    }

    [Fact]
    public void CreateFirst_WithMissingParent_ThrowsParentNotFound()
    {
        // Arrange
        var file = NewFile("proj", "orphan", "content") with { Parent = "ghost" };

        // Act
        var act = () => _repository.CreateFirst(file);

        // Assert
        var error = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.ParentNotFound, error.Code);
        Assert.Equal("proj", error.Project);
        Assert.Equal("ghost", error.Parent);
    }

    [Fact]
    public void CommitVersion_MatchingBase_CommitsNextVersion()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "note-a", "v1"));

        // Act
        var committed = _repository.CommitVersion(Write("proj", "note-a", baseVersion: 1, "v2 content"));

        // Assert
        Assert.Equal(2, committed.Version);
        Assert.Equal(2, _repository.GetCurrent("proj", "note-a").CurrentVersion);
    }

    [Fact]
    public void CommitVersion_StaleBase_ThrowsConflictWithCurrentVersion()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "note-a", "v1"));
        _repository.CommitVersion(Write("proj", "note-a", baseVersion: 1, "v2"));

        // Act
        var act = () => _repository.CommitVersion(Write("proj", "note-a", baseVersion: 1, "stale"));

        // Assert
        var conflict = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.Conflict, conflict.Code);
        Assert.Equal(2, conflict.CurrentVersion);
    }

    [Fact]
    public void CommitVersion_MissingFile_ThrowsNotFound()
    {
        // Act
        var act = () => _repository.CommitVersion(Write("proj", "ghost", baseVersion: 1, "content"));

        // Assert
        var error = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.NotFound, error.Code);
    }

    [Fact]
    public void CommitVersion_ArchivedFileWithStaleBase_ThrowsArchivedNotConflict()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "note-a", "v1"));
        _repository.CommitVersion(Write("proj", "note-a", baseVersion: 1, "v2"));
        _repository.SetState("proj", "note-a", FileState.Archived);

        // Act
        var act = () => _repository.CommitVersion(Write("proj", "note-a", baseVersion: 1, "stale"));

        // Assert
        var error = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.Archived, error.Code);
    }

    [Fact]
    public void CommitVersion_NullSummary_KeepsExistingSummary()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "note-a", "v1") with { Summary = "original summary" });

        // Act
        _repository.CommitVersion(Write("proj", "note-a", baseVersion: 1, "v2") with { Summary = null });

        // Assert
        Assert.Equal("original summary", _repository.GetCurrent("proj", "note-a").Summary);
    }

    [Fact]
    public void CommitVersion_NullTags_KeepsExistingTags()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "note-a", "v1") with { Tags = ["keep", "me"] });

        // Act
        _repository.CommitVersion(Write("proj", "note-a", baseVersion: 1, "v2") with { Tags = null });

        // Assert
        Assert.Equal(["keep", "me"], _repository.GetCurrent("proj", "note-a").Tags);
    }

    [Fact]
    public void CommitVersion_NewTags_ReplaceOldSetAndResyncFts()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "note-a", "v1") with { Tags = ["oldtag"] });

        // Act
        _repository.CommitVersion(Write("proj", "note-a", baseVersion: 1, "v2") with { Tags = ["newtag"] });

        // Assert
        Assert.Equal(["newtag"], _repository.GetCurrent("proj", "note-a").Tags);
        Assert.Contains("note-a", ListNames(query: "newtag"));
        Assert.Empty(ListNames(query: "oldtag"));
    }

    [Fact]
    public void SetMeta_SummaryChange_UpdatesLiveSummaryButNotHistory()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "note-a", "v1") with { Summary = "captured at v1" });

        // Act
        _repository.SetMeta(new MetaUpdate
        {
            Project = "proj",
            Name = "note-a",
            Summary = "live summary",
        });

        // Assert
        Assert.Equal("live summary", _repository.GetCurrent("proj", "note-a").Summary);
        Assert.Equal("captured at v1", _repository.GetVersion("proj", "note-a", 1).Summary);
        Assert.Equal("captured at v1", _repository.History("proj", "note-a")[0].Summary);
    }

    [Fact]
    public void SetMeta_Always_ReturnsUnchangedCurrentVersion()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "note-a", "v1"));

        // Act
        var version = _repository.SetMeta(new MetaUpdate { Project = "proj", Name = "note-a", Summary = "s" });

        // Assert
        Assert.Equal(1, version);
        Assert.Equal(1, _repository.GetCurrent("proj", "note-a").CurrentVersion);
    }

    [Fact]
    public void SetMeta_TagsOnly_BumpsUpdatedAt()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "note-a", "v1"));
        SetUpdatedAt("proj", "note-a", 1_000);

        // Act
        _repository.SetMeta(new MetaUpdate { Project = "proj", Name = "note-a", Tags = ["fresh"] });

        // Assert
        Assert.True(ReadUpdatedAt("proj", "note-a") > 1_000);
    }

    [Fact]
    public void SetMeta_TagChange_ResyncsFts()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "note-a", "v1") with { Tags = ["oldtag"] });

        // Act
        _repository.SetMeta(new MetaUpdate { Project = "proj", Name = "note-a", Tags = ["newtag"] });

        // Assert
        Assert.Contains("note-a", ListNames(query: "newtag"));
        Assert.Empty(ListNames(query: "oldtag"));
    }

    [Fact]
    public void SetMeta_ParentOnly_LeavesFtsRowUntouched()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "parent-note", "root"));
        _repository.CreateFirst(NewFile("proj", "note-a", "v1") with { Summary = "findable summary" });
        var ftsBefore = ReadFtsRow("proj", "note-a");

        // Act
        _repository.SetMeta(new MetaUpdate
        {
            Project = "proj",
            Name = "note-a",
            Parent = ParentUpdate.Set("parent-note"),
        });

        // Assert
        Assert.Equal(ftsBefore, ReadFtsRow("proj", "note-a"));
        Assert.Contains("note-a", ListNames(query: "findable"));
    }

    [Fact]
    public void SetMeta_StaleBaseVersionGuard_ThrowsHintlessConflict()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "note-a", "v1"));
        _repository.CommitVersion(Write("proj", "note-a", baseVersion: 1, "v2"));

        // Act
        Action act = () => _repository.SetMeta(new MetaUpdate
        {
            Project = "proj",
            Name = "note-a",
            Summary = "stale write",
            BaseVersion = 1,
        });

        // Assert
        var conflict = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.Conflict, conflict.Code);
        Assert.Equal(2, conflict.CurrentVersion);
        Assert.Null(conflict.Diff);
    }

    [Fact]
    public void SetMeta_ArchivedFile_ThrowsArchived()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "note-a", "v1"));
        _repository.SetState("proj", "note-a", FileState.Archived);

        // Act
        Action act = () => _repository.SetMeta(new MetaUpdate { Project = "proj", Name = "note-a", Summary = "s" });

        // Assert
        var error = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.Archived, error.Code);
    }

    [Fact]
    public void SetMeta_SetParent_SelfLink_ThrowsInvalidParent()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "note-a", "v1"));

        // Act
        Action act = () => _repository.SetMeta(new MetaUpdate
        {
            Project = "proj",
            Name = "note-a",
            Parent = ParentUpdate.Set("note-a"),
        });

        // Assert
        var error = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.InvalidParent, error.Code);
    }

    [Fact]
    public void SetMeta_SetParent_DirectCycle_ThrowsInvalidParent()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "note-a", "a"));
        _repository.CreateFirst(NewFile("proj", "note-b", "b") with { Parent = "note-a" });

        // Act
        Action act = () => _repository.SetMeta(new MetaUpdate
        {
            Project = "proj",
            Name = "note-a",
            Parent = ParentUpdate.Set("note-b"),
        });

        // Assert
        var error = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.InvalidParent, error.Code);
    }

    [Fact]
    public void SetMeta_SetParent_DeepCycle_ThrowsInvalidParent()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "note-a", "a"));
        _repository.CreateFirst(NewFile("proj", "note-b", "b") with { Parent = "note-a" });
        _repository.CreateFirst(NewFile("proj", "note-c", "c") with { Parent = "note-b" });

        // Act
        Action act = () => _repository.SetMeta(new MetaUpdate
        {
            Project = "proj",
            Name = "note-a",
            Parent = ParentUpdate.Set("note-c"),
        });

        // Assert
        var error = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.InvalidParent, error.Code);
    }

    [Fact]
    public void SetMeta_SetParent_CrossProject_ThrowsParentNotFound()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj-a", "note", "a"));
        _repository.CreateFirst(NewFile("proj-b", "would-be-parent", "b"));

        // Act
        Action act = () => _repository.SetMeta(new MetaUpdate
        {
            Project = "proj-a",
            Name = "note",
            Parent = ParentUpdate.Set("would-be-parent"),
        });

        // Assert
        var error = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.ParentNotFound, error.Code);
    }

    [Fact]
    public void SetMeta_ClearParent_DetachesToTopLevel()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "parent-note", "root"));
        _repository.CreateFirst(NewFile("proj", "child-note", "leaf") with { Parent = "parent-note" });

        // Act
        _repository.SetMeta(new MetaUpdate
        {
            Project = "proj",
            Name = "child-note",
            Parent = ParentUpdate.Clear,
            Summary = "also change something visible",
        });

        // Assert
        Assert.Null(_repository.GetCurrent("proj", "child-note").Parent);
        Assert.Empty(_repository.Children("proj", "parent-note"));
    }

    [Fact]
    public void Children_ReturnsActiveOnly_NameSorted()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "parent-note", "root"));
        _repository.CreateFirst(NewFile("proj", "zeta", "z") with { Parent = "parent-note" });
        _repository.CreateFirst(NewFile("proj", "alpha", "a") with { Parent = "parent-note" });
        _repository.CreateFirst(NewFile("proj", "mid", "m") with { Parent = "parent-note" });
        _repository.SetState("proj", "mid", FileState.Archived);

        // Act
        var children = _repository.Children("proj", "parent-note");

        // Assert
        Assert.Equal(["alpha", "zeta"], children.Select(c => c.Name));
    }

    [Fact]
    public void GetCurrent_MissingFile_ThrowsNotFound()
    {
        // Act
        var act = () => _repository.GetCurrent("proj", "ghost");

        // Assert
        var error = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.NotFound, error.Code);
        Assert.Equal("ghost", error.Name);
    }

    [Fact]
    public void GetVersion_MissingVersion_ThrowsNotFoundWithVersionInName()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "note-a", "v1"));

        // Act
        var act = () => _repository.GetVersion("proj", "note-a", 9);

        // Assert
        var error = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.NotFound, error.Code);
        Assert.Equal("note-a (version 9)", error.Name);
    }

    [Fact]
    public void GetCurrent_ArchivedFile_StillWorksAndReportsState()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "note-a", "v1"));
        _repository.SetState("proj", "note-a", FileState.Archived);

        // Act
        var record = _repository.GetCurrent("proj", "note-a");

        // Assert
        Assert.Equal(FileState.Archived, record.State);
    }

    [Fact]
    public void History_ReturnsNewestFirst()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "note-a", "v1"));
        _repository.CommitVersion(Write("proj", "note-a", baseVersion: 1, "v2") with { Op = VaultOp.Append });

        // Act
        var history = _repository.History("proj", "note-a");

        // Assert
        Assert.Equal([2, 1], history.Select(h => h.Version));
        Assert.Equal(VaultOp.Append, history[0].Op);
        Assert.Equal(VaultOp.Save, history[1].Op);
    }

    [Fact]
    public void History_UnknownPersistedOp_ParsesAsSave()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "note-a", "v1"));
        using (var connection = _factory.Open())
        {
            connection.Execute("UPDATE versions SET op = 'mystery_op'");
        }

        // Act
        var history = _repository.History("proj", "note-a");

        // Assert
        var row = Assert.Single(history);
        Assert.Equal(VaultOp.Save, row.Op);
    }

    [Fact]
    public void SetState_MissingFile_ThrowsNotFound()
    {
        // Act
        var act = () => _repository.SetState("proj", "ghost", FileState.Archived);

        // Assert
        var error = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.NotFound, error.Code);
    }

    [Fact]
    public void SetState_Rearchive_SucceedsUnconditionally()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "note-a", "v1"));
        _repository.SetState("proj", "note-a", FileState.Archived);

        // Act
        var exception = Record.Exception(() => _repository.SetState("proj", "note-a", FileState.Archived));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void List_NoFilters_ReturnsActiveFilesOrderedByUpdatedAtDescThenName()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "old-note", "a"));
        _repository.CreateFirst(NewFile("proj", "new-b", "b"));
        _repository.CreateFirst(NewFile("proj", "new-a", "c"));
        _repository.CreateFirst(NewFile("proj", "hidden", "d"));
        _repository.SetState("proj", "hidden", FileState.Archived);
        SetUpdatedAt("proj", "old-note", 1_000);
        SetUpdatedAt("proj", "new-b", 2_000);
        SetUpdatedAt("proj", "new-a", 2_000);

        // Act
        var rows = _repository.List(new ListFilter());

        // Assert
        Assert.Equal(["new-a", "new-b", "old-note"], rows.Select(r => r.Name));
    }

    [Fact]
    public void List_ProjectFilter_ScopesToProject()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj-a", "note-a", "a"));
        _repository.CreateFirst(NewFile("proj-b", "note-b", "b"));

        // Act
        var rows = _repository.List(new ListFilter { Project = "proj-a" });

        // Assert
        var row = Assert.Single(rows);
        Assert.Equal("note-a", row.Name);
    }

    [Fact]
    public void List_TagFilter_RequiresAllTags()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "both", "a") with { Tags = ["x", "y"] });
        _repository.CreateFirst(NewFile("proj", "only-x", "b") with { Tags = ["x"] });

        // Act
        var rows = _repository.List(new ListFilter { Tags = ["x", "y"] });

        // Assert
        var row = Assert.Single(rows);
        Assert.Equal("both", row.Name);
    }

    [Fact]
    public void List_QueryMatchingSummary_ReturnsHit()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "note-a", "a") with { Summary = "quantum flamingo notes" });
        _repository.CreateFirst(NewFile("proj", "note-b", "b") with { Summary = "unrelated" });

        // Act
        var rows = _repository.List(new ListFilter { Query = "flamingo" });

        // Assert
        var row = Assert.Single(rows);
        Assert.Equal("note-a", row.Name);
    }

    [Fact]
    public void List_WhitespaceOnlyQuery_MeansNoFilterNotZeroResults()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "note-a", "a"));

        // Act
        var rows = _repository.List(new ListFilter { Query = "   " });

        // Assert
        Assert.Single(rows);
    }

    [Fact]
    public void List_QueryWithNoHits_ReturnsEmpty()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "note-a", "a"));

        // Act
        var rows = _repository.List(new ListFilter { Query = "zzzzznomatch" });

        // Assert
        Assert.Empty(rows);
    }

    [Fact]
    public void List_ReturnsParentNameOnChildRows()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "parent-note", "root"));
        _repository.CreateFirst(NewFile("proj", "child-note", "leaf") with { Parent = "parent-note" });

        // Act
        var rows = _repository.List(new ListFilter { Project = "proj" });

        // Assert
        Assert.Equal("parent-note", rows.Single(r => r.Name == "child-note").Parent);
        Assert.Null(rows.Single(r => r.Name == "parent-note").Parent);
    }

    [Theory]
    [InlineData("\"")]
    [InlineData("a\" OR \"b")]
    [InlineData("NEAR(x,y)")]
    [InlineData("name:x")]
    [InlineData("-")]
    [InlineData("(unbalanced")]
    [InlineData("term*")]
    public void List_HostileFtsQuery_DoesNotThrow(string query)
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "note-a", "a"));

        // Act
        var exception = Record.Exception(() => _repository.List(new ListFilter { Query = query }));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void BuildFtsQuery_QuotesAndJoinsTokens()
    {
        // Act + Assert
        Assert.Equal("\"hello\" \"world\"", SqliteVaultRepository.BuildFtsQuery("hello world"));
        Assert.Equal("\"say\" \"\"\"hi\"\"\"", SqliteVaultRepository.BuildFtsQuery("say \"hi\""));
        Assert.Null(SqliteVaultRepository.BuildFtsQuery("   "));
        Assert.Null(SqliteVaultRepository.BuildFtsQuery(null));
    }

    [Fact]
    public void Purge_RemovesFileVersionsTagsAndFtsRow()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "note-a", "v1") with { Tags = ["t1"], Summary = "searchable" });
        _repository.CommitVersion(Write("proj", "note-a", baseVersion: 1, "v2"));

        // Act
        var relPaths = _repository.Purge("proj", "note-a");

        // Assert
        Assert.Equal(2, relPaths.Count);
        var act = () => _repository.GetCurrent("proj", "note-a");
        var error = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.NotFound, error.Code);
        Assert.Empty(ListNames(query: "searchable"));
        using var connection = _factory.Open();
        Assert.Equal(0, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM versions"));
        Assert.Equal(0, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM tags"));
        Assert.Equal(0, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM files_fts"));
    }

    [Fact]
    public void Purge_MissingFile_ThrowsNotFound()
    {
        // Act
        var act = () => _repository.Purge("proj", "ghost");

        // Assert
        var error = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.NotFound, error.Code);
    }

    [Fact]
    public void Purge_SharedRelPath_ExcludedFromReturnedPaths()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "note-a", "v1"));
        _repository.CreateFirst(NewFile("proj", "note-b", "v1"));
        using (var connection = _factory.Open())
        {
            var bPath = connection.ExecuteScalar<string>(
                "SELECT v.rel_path FROM versions v JOIN files f ON f.id = v.file_id WHERE f.name = 'note-b'");
            connection.Execute(
                "UPDATE versions SET rel_path = @path WHERE file_id = (SELECT id FROM files WHERE name = 'note-a')",
                new { path = bPath.ToUpperInvariant() });
        }

        // Act
        var relPaths = _repository.Purge("proj", "note-a");

        // Assert
        Assert.Empty(relPaths);
    }

    [Fact]
    public void Purge_OrphansChildrenInsteadOfCascading()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "parent-note", "root"));
        _repository.CreateFirst(NewFile("proj", "child-note", "leaf") with { Parent = "parent-note" });

        // Act
        _repository.Purge("proj", "parent-note");

        // Assert
        Assert.Null(_repository.GetCurrent("proj", "child-note").Parent);
    }

    [Fact]
    public void StillReferenced_EmptyInput_ReturnsEmpty()
    {
        // Act
        var referenced = _repository.StillReferenced([]);

        // Assert
        Assert.Empty(referenced);
    }

    [Fact]
    public void StillReferenced_LivePathQueriedWithDifferentCasing_ReturnsStoredPath()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "note-a", "v1"));
        var storedPath = _repository.GetCurrent("proj", "note-a").RelPath;

        // Act
        var referenced = _repository.StillReferenced([storedPath.ToUpperInvariant()]);

        // Assert
        var path = Assert.Single(referenced);
        Assert.Equal(storedPath, path);
    }

    [Fact]
    public void StillReferenced_UnreferencedPath_IsNotReturned()
    {
        // Arrange
        _repository.CreateFirst(NewFile("proj", "note-a", "v1"));

        // Act
        var referenced = _repository.StillReferenced(["proj/ghost/v0001-deadbeef.txt"]);

        // Assert
        Assert.Empty(referenced);
    }

    private List<string> ListNames(string query)
        => _repository.List(new ListFilter { Query = query }).Select(r => r.Name).ToList();

    private void SetUpdatedAt(string project, string name, long updatedAt)
    {
        using var connection = _factory.Open();
        connection.Execute(
            "UPDATE files SET updated_at = @updatedAt WHERE project = @project AND name = @name",
            new { updatedAt, project, name });
    }

    private long ReadUpdatedAt(string project, string name)
    {
        using var connection = _factory.Open();
        return connection.ExecuteScalar<long>(
            "SELECT updated_at FROM files WHERE project = @project AND name = @name",
            new { project, name });
    }

    private string ReadFtsRow(string project, string name)
    {
        using var connection = _factory.Open();
        return connection.ExecuteScalar<string>(
            "SELECT name || '|' || summary || '|' || tags FROM files_fts "
            + "WHERE rowid = (SELECT id FROM files WHERE project = @project AND name = @name)",
            new { project, name });
    }

    internal static VaultConfig TestConfig(string home)
        => new()
        {
            Home = home,
            DbPath = Path.Combine(home, "vault.db"),
            FilesDir = Path.Combine(home, "files"),
            MaxContentBytes = VaultConfig.DefaultMaxContentBytes,
            BusyTimeoutMs = VaultConfig.DefaultBusyTimeoutMs,
            SplitHintChars = 0, // hint disabled; 
        };

    private static NewFile NewFile(string project, string name, string content)
        => new()
        {
            Project = project,
            Name = name,
            Format = VaultFormat.Text,
            Summary = "summary of " + name,
            Tags = [],
            Hash = ContentHash.OfText(content),
            RelPath = $"{project}/{name}/v0001-{ContentHash.OfText(content).ShortHex}.txt",
            ByteSize = content.Length,
            CreatedAt = 1_700_000_000,
        };

    private static VersionedWrite Write(string project, string name, int baseVersion, string content)
        => new()
        {
            Project = project,
            Name = name,
            BaseVersion = baseVersion,
            NewVersion = baseVersion + 1,
            Format = VaultFormat.Text,
            Summary = "summary v" + (baseVersion + 1),
            Hash = ContentHash.OfText(content),
            RelPath = $"{project}/{name}/v{baseVersion + 1:D4}-{ContentHash.OfText(content).ShortHex}.txt",
            ByteSize = content.Length,
            Op = VaultOp.Save,
            UpdatedAt = 1_700_000_100,
        };
}