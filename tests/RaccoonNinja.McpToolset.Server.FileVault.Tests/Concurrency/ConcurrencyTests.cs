using RaccoonNinja.McpToolset.Server.FileVault.Domain;
using RaccoonNinja.McpToolset.Server.FileVault.Errors;
using RaccoonNinja.McpToolset.Server.FileVault.Services;
using RaccoonNinja.McpToolset.Server.FileVault.Storage;
using RaccoonNinja.McpToolset.Server.FileVault.Tests.Storage;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tests.Concurrency;

/// <summary>
/// Concurrency tests over one real store: OCC must admit exactly one winner per base version,
/// independent writers must not interfere, and two independently opened stores on the same home
/// must both work (the WAL + BEGIN IMMEDIATE contract).
/// </summary>
public sealed class ConcurrencyTests : IDisposable
{
    private readonly string _home;
    private readonly VaultService _service;

    public ConcurrencyTests()
    {
        _home = Path.Combine(Path.GetTempPath(), "filevault-tests", Guid.NewGuid().ToString("N"));
        var config = SqliteVaultRepositoryTests.TestConfig(_home);
        Directory.CreateDirectory(config.FilesDir);
        var factory = new SqliteConnectionFactory(config);
        Migrator.Run(factory);
        _service = new VaultService(new SqliteVaultRepository(factory), new FileStore(config.FilesDir), config);
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
    public async Task Save_ParallelWritersOnOneBaseVersion_ExactlyOneWins()
    {
        // Arrange
        var project = ProjectName.Parse("proj");
        var name = FileName.Parse("contested");
        _service.Save(project, name, "v1", "s", null, null, VaultFormat.Text, ParentUpdate.Leave);

        // Act
        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() =>
        {
            try
            {
                _service.Save(project, name, $"winner {i}", "s", 1, null, VaultFormat.Text, ParentUpdate.Leave);
                return "ok";
            }
            catch (VaultException ex) when (ex.Code == VaultErrorCode.Conflict)
            {
                return "conflict";
            }
        })));

        // Assert
        Assert.Equal(1, attempts.Count(a => a == "ok"));
        Assert.Equal(7, attempts.Count(a => a == "conflict"));
        Assert.Equal(2, _service.Get(project, name, version: null).Record.CurrentVersion);
    }

    [Fact]
    public async Task Save_ParallelFirstSavesOfOneName_ExactlyOneWins()
    {
        // Arrange
        var project = ProjectName.Parse("proj");
        var name = FileName.Parse("birth-race");

        // Act
        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() =>
        {
            try
            {
                _service.Save(project, name, $"first {i}", "s", null, null, VaultFormat.Text, ParentUpdate.Leave);
                return "ok";
            }
            catch (VaultException ex) when (ex.Code == VaultErrorCode.Conflict)
            {
                return "conflict";
            }
        })));

        // Assert
        Assert.Equal(1, attempts.Count(a => a == "ok"));
        Assert.Equal(1, _service.Get(project, name, version: null).Record.CurrentVersion);
    }

    [Fact]
    public async Task Save_ParallelDistinctFiles_AllSucceed()
    {
        // Arrange
        var project = ProjectName.Parse("proj");

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(i => Task.Run(() =>
            _service.Save(
                project, FileName.Parse($"note-{i}"), $"content {i}", "s", null, null,
                VaultFormat.Text, ParentUpdate.Leave))));

        // Assert
        Assert.All(results, committed => Assert.Equal(1, committed.Version));
        Assert.Equal(16, _service.List(project, tags: null, query: null).Count);
    }

    [Fact]
    public async Task Save_TwoIndependentlyOpenedStores_ShareOneHomeSafely()
    {
        // Arrange
        var config = SqliteVaultRepositoryTests.TestConfig(_home);
        var otherFactory = new SqliteConnectionFactory(config);
        var other = new VaultService(
            new SqliteVaultRepository(otherFactory), new FileStore(config.FilesDir), config);
        var project = ProjectName.Parse("proj");
        var name = FileName.Parse("shared-note");
        _service.Save(project, name, "v1", "s", null, null, VaultFormat.Text, ParentUpdate.Leave);

        // Act
        var attempts = await Task.WhenAll(
            Task.Run(() => TrySave(_service, project, name, "from stack one")),
            Task.Run(() => TrySave(other, project, name, "from stack two")));

        // Assert
        Assert.Equal(1, attempts.Count(a => a == "ok"));
        Assert.Equal(2, _service.Get(project, name, version: null).Record.CurrentVersion);
        Assert.Equal(2, other.Get(project, name, version: null).Record.CurrentVersion);
    }

    [Fact]
    public void SetMeta_InterleavedWithAppend_MetaSummaryWinsAndVersionCountsAppendsOnly()
    {
        // Arrange
        var project = ProjectName.Parse("proj");
        var name = FileName.Parse("meta-vs-append");
        _service.Save(project, name, "base", "original summary", null, null, VaultFormat.Text, ParentUpdate.Leave);

        // Act
        _service.Append(project, name, " more", baseVersion: 1);
        _service.SetMeta(project, name, "meta summary", tags: null, ParentUpdate.Leave, baseVersion: null);

        // Assert
        var (record, content, _) = _service.Get(project, name, version: null);
        Assert.Equal(2, record.CurrentVersion);
        Assert.Equal("meta summary", record.Summary);
        Assert.Equal("base more", content);
    }

    private static string TrySave(VaultService service, ProjectName project, FileName name, string content)
    {
        try
        {
            service.Save(project, name, content, "s", 1, null, VaultFormat.Text, ParentUpdate.Leave);
            return "ok";
        }
        catch (VaultException ex) when (ex.Code == VaultErrorCode.Conflict)
        {
            return "conflict";
        }
    }
}