using RaccoonNinja.McpToolset.Server.SkillStats.Models;
using RaccoonNinja.McpToolset.Server.SkillStats.Tests.TestSupport;

namespace RaccoonNinja.McpToolset.Server.SkillStats.Tests.Tools;

public sealed class UsageSummaryToolTests
{
    [Fact]
    public async Task InvokeAsync_SeededStore_ReportsTotalsSchemaAndDbSize()
    {
        // Arrange
        using var harness = new SkillStatsHarness();
        harness.Migrate();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        harness.Seed("csharp", now);
        harness.Seed("csharp", now);
        harness.Seed("python", now);

        // Act
        var envelope = await harness.UsageSummary.InvokeAsync();

        // Assert
        var summary = Assert.Single(envelope.Results.Cast<UsageSummaryResult>());
        Assert.Equal(3, summary.TotalRecords);
        Assert.Equal(2, summary.DistinctSkills);
        Assert.Equal(1, summary.SchemaVersion);
        Assert.True(summary.DbSizeBytes > 0);
        Assert.False(summary.HomeOverridden);
        Assert.Equal(0, summary.ErrorLogSizeBytes);
        Assert.Null(summary.ErrorLogLastWrite);
    }

    [Fact]
    public async Task InvokeAsync_HomeOverridden_ReflectsFlag()
    {
        // Arrange
        using var harness = new SkillStatsHarness(homeOverridden: true);
        harness.Migrate();

        // Act
        var envelope = await harness.UsageSummary.InvokeAsync();

        // Assert
        Assert.True(Assert.Single(envelope.Results.Cast<UsageSummaryResult>()).HomeOverridden);
    }

    [Fact]
    public async Task InvokeAsync_ErrorLogPresent_ReportsSizeAndLastWrite()
    {
        // Arrange
        using var harness = new SkillStatsHarness();
        harness.Migrate();
        File.WriteAllText(harness.Config.ErrorLogPath, "2026-01-01T00:00:00Z parse JsonException: boom\n");

        // Act
        var envelope = await harness.UsageSummary.InvokeAsync();

        // Assert
        var summary = Assert.Single(envelope.Results.Cast<UsageSummaryResult>());
        Assert.True(summary.ErrorLogSizeBytes > 0);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$", summary.ErrorLogLastWrite);
    }
}