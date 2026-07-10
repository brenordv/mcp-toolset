using RaccoonNinja.McpToolset.Server.FileVault.Storage;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tests.Storage;

/// <summary>
/// Tests for <see cref="Migrator"/>: a fresh database reaches schema version 2 and re-running is
/// idempotent (store compatibility forbids any schema divergence from the Rust server's 0001/0002).
/// </summary>
public sealed class MigratorTests : IDisposable
{
    private readonly string _home;
    private readonly SqliteConnectionFactory _factory;

    public MigratorTests()
    {
        _home = Path.Combine(Path.GetTempPath(), "filevault-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_home);
        _factory = new SqliteConnectionFactory(SqliteVaultRepositoryTests.TestConfig(_home));
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
    public void Run_FreshDatabase_AppliesBothMigrations()
    {
        // Act
        var applied = Migrator.Run(_factory);

        // Assert
        Assert.Equal(2, applied);
        using var connection = _factory.Open();
        Assert.Equal(2, Migrator.CurrentVersion(connection));
    }

    [Fact]
    public void Run_SecondRun_AppliesNothing()
    {
        // Arrange
        Migrator.Run(_factory);

        // Act
        var applied = Migrator.Run(_factory);

        // Assert
        Assert.Equal(0, applied);
        using var connection = _factory.Open();
        Assert.Equal(2, Migrator.CurrentVersion(connection));
    }

    [Fact]
    public void CurrentVersion_BrandNewDatabase_ReturnsZero()
    {
        // Arrange
        using var connection = _factory.Open();

        // Act
        var version = Migrator.CurrentVersion(connection);

        // Assert
        Assert.Equal(0, version);
    }

    [Fact]
    public void Run_CreatesTheExpectedTables()
    {
        // Arrange
        Migrator.Run(_factory);

        // Act
        using var connection = _factory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type IN ('table') ORDER BY name";
        var tables = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        // Assert
        Assert.Contains("files", tables);
        Assert.Contains("versions", tables);
        Assert.Contains("tags", tables);
        Assert.Contains("meta", tables);
        Assert.Contains("files_fts", tables);
    }
}