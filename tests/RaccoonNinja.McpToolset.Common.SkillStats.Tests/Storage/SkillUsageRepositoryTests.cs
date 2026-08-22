using Dapper;
using Microsoft.Data.Sqlite;
using RaccoonNinja.McpToolset.Common.SkillStats.Storage;
using RaccoonNinja.McpToolset.Common.SkillStats.Tests.TestSupport;

namespace RaccoonNinja.McpToolset.Common.SkillStats.Tests.Storage;

public sealed class SkillUsageRepositoryTests
{
    private static readonly long Now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    [Fact]
    public void Insert_ThenRead_RoundtripsFieldsIncludingNulls()
    {
        // Arrange
        using var store = new TempStore();
        Migrator.Run(store.Factory);
        var repository = new SkillUsageRepository(store.Factory);
        repository.Insert(new SkillUsageRecord { UsedAt = Now, Skill = "csharp", Args = null, SessionId = null, Cwd = null });

        // Act
        var rows = repository.UsageForSkill("csharp", cutoffEpoch: null, limit: 10);

        // Assert
        var row = Assert.Single(rows);
        Assert.Equal(Now, row.UsedAt);
        Assert.Equal("csharp", row.Skill);
        Assert.Null(row.Args);
        Assert.Null(row.SessionId);
        Assert.Null(row.Cwd);
    }

    [Fact]
    public void Insert_ArgsOverLimit_TruncatesToMax()
    {
        // Arrange
        using var store = new TempStore();
        Migrator.Run(store.Factory);
        var repository = new SkillUsageRepository(store.Factory);
        var longArgs = new string('x', SkillUsageRepository.MaxArgsChars + 500);
        repository.Insert(new SkillUsageRecord { UsedAt = Now, Skill = "csharp", Args = longArgs });

        // Act
        var row = Assert.Single(repository.UsageForSkill("csharp", cutoffEpoch: null, limit: 10));

        // Assert
        Assert.Equal(SkillUsageRepository.MaxArgsChars, row.Args.Length);
    }

    [Fact]
    public void Insert_HostileSkillName_RoundtripsAsDataAndTableSurvives()
    {
        // Arrange
        using var store = new TempStore();
        Migrator.Run(store.Factory);
        var repository = new SkillUsageRepository(store.Factory);
        const string hostile = "'; DROP TABLE skill_usage;--";
        repository.Insert(new SkillUsageRecord { UsedAt = Now, Skill = hostile });

        // Act
        var row = Assert.Single(repository.UsageForSkill(hostile, cutoffEpoch: null, limit: 10));
        var summary = repository.Summary();

        // Assert
        Assert.Equal(hostile, row.Skill);
        Assert.Equal(1, summary.TotalRecords);
    }

    [Fact]
    public void TopSkills_OrdersByUseCountDescendingThenSkillAscending()
    {
        // Arrange
        using var store = new TempStore();
        Migrator.Run(store.Factory);
        var repository = new SkillUsageRepository(store.Factory);
        Insert(repository, "beta", 3);
        Insert(repository, "alpha", 2);
        Insert(repository, "zeta", 2);

        // Act
        var top = repository.TopSkills(cutoffEpoch: null, limit: 10);

        // Assert
        Assert.Equal(["beta", "alpha", "zeta"], top.Select(row => row.Skill));
        Assert.Equal(3, top[0].Uses);
    }

    [Fact]
    public void TopSkills_CutoffIsInclusiveAtTheBoundary()
    {
        // Arrange
        using var store = new TempStore();
        Migrator.Run(store.Factory);
        var repository = new SkillUsageRepository(store.Factory);
        var cutoff = Now - 100;
        repository.Insert(new SkillUsageRecord { UsedAt = cutoff, Skill = "onboundary" });
        repository.Insert(new SkillUsageRecord { UsedAt = cutoff - 1, Skill = "beforeboundary" });

        // Act
        var top = repository.TopSkills(cutoff, limit: 10);

        // Assert
        Assert.Equal(["onboundary"], top.Select(row => row.Skill));
    }

    [Fact]
    public void UsageForSkill_ReturnsNewestFirstAndRespectsLimit()
    {
        // Arrange
        using var store = new TempStore();
        Migrator.Run(store.Factory);
        var repository = new SkillUsageRepository(store.Factory);
        repository.Insert(new SkillUsageRecord { UsedAt = Now - 30, Skill = "csharp", Args = "old" });
        repository.Insert(new SkillUsageRecord { UsedAt = Now - 10, Skill = "csharp", Args = "new" });
        repository.Insert(new SkillUsageRecord { UsedAt = Now - 20, Skill = "csharp", Args = "mid" });

        // Act
        var rows = repository.UsageForSkill("csharp", cutoffEpoch: null, limit: 2);

        // Assert
        Assert.Equal(["new", "mid"], rows.Select(row => row.Args));
    }

    [Fact]
    public void Summary_ReportsTotalsAndDistinctSkills()
    {
        // Arrange
        using var store = new TempStore();
        Migrator.Run(store.Factory);
        var repository = new SkillUsageRepository(store.Factory);
        Insert(repository, "csharp", 2);
        Insert(repository, "python", 1);

        // Act
        var summary = repository.Summary();

        // Assert
        Assert.Equal(3, summary.TotalRecords);
        Assert.Equal(2, summary.DistinctSkills);
        Assert.NotNull(summary.FirstUsed);
        Assert.NotNull(summary.LastUsed);
    }

    [Fact]
    public void Summary_EmptyStore_ReportsZerosAndNullBounds()
    {
        // Arrange
        using var store = new TempStore();
        Migrator.Run(store.Factory);
        var repository = new SkillUsageRepository(store.Factory);

        // Act
        var summary = repository.Summary();

        // Assert
        Assert.Equal(0, summary.TotalRecords);
        Assert.Equal(0, summary.DistinctSkills);
        Assert.Null(summary.FirstUsed);
        Assert.Null(summary.LastUsed);
    }

    [Fact]
    public void OpenReader_AttemptedInsert_ThrowsReadOnly()
    {
        // Arrange
        using var store = new TempStore();
        Migrator.Run(store.Factory);
        using var connection = store.Factory.OpenReader();

        // Act
        var act = () => { connection.Execute("INSERT INTO skill_usage(used_at, skill) VALUES(1, 'x')"); };

        // Assert
        Assert.Throws<SqliteException>(act);
    }

    private static void Insert(SkillUsageRepository repository, string skill, int times)
    {
        for (var index = 0; index < times; index++)
        {
            repository.Insert(new SkillUsageRecord { UsedAt = Now - index, Skill = skill });
        }
    }
}