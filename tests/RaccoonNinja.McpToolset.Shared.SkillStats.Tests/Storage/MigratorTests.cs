using Dapper;
using RaccoonNinja.McpToolset.Shared.SkillStats.Storage;
using RaccoonNinja.McpToolset.Shared.SkillStats.Tests.TestSupport;

namespace RaccoonNinja.McpToolset.Shared.SkillStats.Tests.Storage;

public sealed class MigratorTests
{
    [Fact]
    public void Run_FreshDatabase_AppliesMigrationOne()
    {
        // Arrange
        using var store = new TempStore();

        // Act
        var applied = Migrator.Run(store.Factory);

        // Assert
        Assert.Equal(1, applied);
        using var connection = store.Factory.OpenReader();
        Assert.Equal(1, Migrator.CurrentVersion(connection));
    }

    [Fact]
    public void Run_SecondRun_AppliesNothing()
    {
        // Arrange
        using var store = new TempStore();
        Migrator.Run(store.Factory);

        // Act
        var applied = Migrator.Run(store.Factory);

        // Assert
        Assert.Equal(0, applied);
    }

    [Fact]
    public void Run_CreatesTheExpectedTables()
    {
        // Arrange
        using var store = new TempStore();
        Migrator.Run(store.Factory);

        // Act
        using var connection = store.Factory.OpenReader();
        var tables = connection.Query<string>(
            "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name").ToList();

        // Assert
        Assert.Contains("meta", tables);
        Assert.Contains("skill_usage", tables);
    }

    [Fact]
    public async Task Run_SecondMigratorContendsOnWriteLock_WaitsThenAppliesNothing()
    {
        // Arrange
        using var store = new TempStore();
        var contenderFactory = new SqliteConnectionFactory(store.Config);
        using var blocker = store.Factory.OpenWriter();
        using var blockerTransaction = blocker.BeginTransaction();
        blocker.Execute("CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL)", transaction: blockerTransaction);
        blocker.Execute("INSERT INTO meta(key, value) VALUES('schema_version', '1')", transaction: blockerTransaction);
        var contenderStarted = new ManualResetEventSlim(false);
        var contender = Task.Run(() =>
        {
            contenderStarted.Set();
            return Migrator.Run(contenderFactory);
        });

        // Act
        contenderStarted.Wait();
        await Task.Delay(200);
        blockerTransaction.Commit();
        var appliedByContender = await contender;

        // Assert
        Assert.Equal(0, appliedByContender);
    }

    [Fact]
    public void SqliteVersion_MeetsReadOnlyWalFloor()
    {
        // Arrange
        using var store = new TempStore();
        using var connection = store.Factory.OpenWriter();

        // Act
        var version = connection.ExecuteScalar<string>("SELECT sqlite_version()");

        // Assert
        Assert.True(Version.Parse(version) >= new Version(3, 22, 0), $"sqlite_version() was {version}");
    }
}