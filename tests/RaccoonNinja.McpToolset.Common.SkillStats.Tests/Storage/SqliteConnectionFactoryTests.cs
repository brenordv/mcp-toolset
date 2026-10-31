using System.Diagnostics;
using Dapper;
using Microsoft.Data.Sqlite;
using RaccoonNinja.McpToolset.Common.SkillStats.Configuration;
using RaccoonNinja.McpToolset.Common.SkillStats.Storage;
using RaccoonNinja.McpToolset.Common.SkillStats.Tests.TestSupport;

namespace RaccoonNinja.McpToolset.Common.SkillStats.Tests.Storage;

public sealed class SqliteConnectionFactoryTests
{
    private const int SqliteBusy = 5;

    private static readonly TimeSpan HookTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void OpenWriter_AppliesWriterBusyTimeout()
    {
        // Arrange
        using var store = new TempStore();

        // Act
        using var connection = store.Factory.OpenWriter();
        var timeout = connection.ExecuteScalar<long>("PRAGMA busy_timeout;");

        // Assert
        Assert.Equal(SkillStatsConfig.WriterBusyTimeoutMs, timeout);
    }

    [Fact]
    public void OpenReader_AppliesReaderBusyTimeout()
    {
        // Arrange
        using var store = new TempStore();
        Migrator.Run(store.Factory);

        // Act
        using var connection = store.Factory.OpenReader();
        var timeout = connection.ExecuteScalar<long>("PRAGMA busy_timeout;");

        // Assert
        Assert.Equal(SkillStatsConfig.BusyTimeoutMs, timeout);
    }

    [Fact]
    public void OpenWriter_DatabaseHeldByAnotherWriter_GivesUpWithinHookTimeout()
    {
        // Arrange
        using var store = new TempStore();
        Migrator.Run(store.Factory);
        using var blocker = store.Factory.OpenWriter();
        using var contender = store.Factory.OpenWriter();
        using var blockerTransaction = blocker.BeginTransaction();

        // Act
        var stopwatch = Stopwatch.StartNew();
        var exception = Record.Exception(() => contender.BeginTransaction());
        stopwatch.Stop();

        // Assert
        var sqliteException = Assert.IsType<SqliteException>(exception);
        Assert.Equal(SqliteBusy, sqliteException.SqliteErrorCode);
        Assert.True(stopwatch.Elapsed < HookTimeout, $"the contender gave up after {stopwatch.Elapsed}");
    }
}