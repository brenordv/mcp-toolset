using Dapper;
using RaccoonNinja.McpToolset.Shared.SkillStats.Storage;
using RaccoonNinja.McpToolset.Shared.SkillStats.Tests.TestSupport;

namespace RaccoonNinja.McpToolset.Shared.SkillStats.Tests.Storage;

public sealed class ConcurrencyTests
{
    private static readonly long Now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    [Fact]
    public void Reader_DuringUncommittedWrite_SeesOnlyCommittedRows()
    {
        // Arrange
        using var store = new TempStore();
        Migrator.Run(store.Factory);
        new SkillUsageRepository(store.Factory).Insert(new SkillUsageRecord { UsedAt = Now, Skill = "committed" });
        using var writer = store.Factory.OpenWriter();
        using var transaction = writer.BeginTransaction();
        writer.Execute(
            "INSERT INTO skill_usage(used_at, skill) VALUES(@used, 'pending')",
            new { used = Now },
            transaction);

        // Act
        long visibleCount;
        using (var reader = store.Factory.OpenReader())
        {
            visibleCount = reader.ExecuteScalar<long>("SELECT COUNT(*) FROM skill_usage");
        }

        // Assert
        Assert.Equal(1L, visibleCount);
    }

    [Fact]
    public void TwoSequentialWriters_BothRowsPersist()
    {
        // Arrange
        using var store = new TempStore();
        Migrator.Run(store.Factory);

        // Act
        new SkillUsageRepository(store.Factory).Insert(new SkillUsageRecord { UsedAt = Now, Skill = "a" });
        new SkillUsageRepository(new SqliteConnectionFactory(store.Config)).Insert(new SkillUsageRecord { UsedAt = Now, Skill = "b" });

        // Assert
        using var reader = store.Factory.OpenReader();
        var count = reader.ExecuteScalar<long>("SELECT COUNT(*) FROM skill_usage");
        Assert.Equal(2L, count);
    }
}