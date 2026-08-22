using RaccoonNinja.McpToolset.Shared.SkillStats.Configuration;

namespace RaccoonNinja.McpToolset.Shared.SkillStats.Tests.Configuration;

public sealed class SkillStatsConfigTests
{
    private const string HomeEnvVar = "SKILL_STATS_HOME";

    [Fact]
    public void WithHome_DerivesDatabaseAndErrorLogPaths()
    {
        // Arrange
        var home = Path.Combine(Path.GetTempPath(), "skillstats-config-" + Guid.NewGuid().ToString("N"));

        // Act
        var config = SkillStatsConfig.WithHome(home, homeOverridden: false);

        // Assert
        Assert.Equal(home, config.Home);
        Assert.Equal(Path.Combine(home, "skill-stats.db"), config.DbPath);
        Assert.Equal(Path.Combine(home, "ingest-errors.log"), config.ErrorLogPath);
        Assert.False(config.HomeOverridden);
    }

    [Fact]
    public void WithHome_HomeOverriddenFlag_IsCarried()
    {
        // Arrange
        var home = Path.Combine(Path.GetTempPath(), "skillstats-config-" + Guid.NewGuid().ToString("N"));

        // Act
        var config = SkillStatsConfig.WithHome(home, homeOverridden: true);

        // Assert
        Assert.True(config.HomeOverridden);
    }

    [Fact]
    public void Load_WithSkillStatsHomeSet_UsesItAndMarksOverridden()
    {
        // Arrange
        var original = Environment.GetEnvironmentVariable(HomeEnvVar);
        var home = Path.Combine(Path.GetTempPath(), "skillstats-config-" + Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable(HomeEnvVar, home);

            // Act
            var config = SkillStatsConfig.Load();

            // Assert
            Assert.Equal(home, config.Home);
            Assert.True(config.HomeOverridden);
        }
        finally
        {
            Environment.SetEnvironmentVariable(HomeEnvVar, original);
        }
    }

    [Fact]
    public void Load_WithoutOverride_DefaultsUnderUserProfileAndNotOverridden()
    {
        // Arrange
        var original = Environment.GetEnvironmentVariable(HomeEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(HomeEnvVar, null);

            // Act
            var config = SkillStatsConfig.Load();

            // Assert
            Assert.EndsWith(".skill-stats", config.Home);
            Assert.False(config.HomeOverridden);
        }
        finally
        {
            Environment.SetEnvironmentVariable(HomeEnvVar, original);
        }
    }
}