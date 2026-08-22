using RaccoonNinja.McpToolset.Server.SkillStats.Errors;
using RaccoonNinja.McpToolset.Server.SkillStats.Models;
using RaccoonNinja.McpToolset.Server.SkillStats.Tests.TestSupport;

namespace RaccoonNinja.McpToolset.Server.SkillStats.Tests.Tools;

public sealed class TopSkillsToolTests
{
    private const int SecondsPerDay = 86_400;

    [Fact]
    public async Task InvokeAsync_OrdersByUseCountThenName()
    {
        // Arrange
        using var harness = new SkillStatsHarness();
        harness.Migrate();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        SeedTimes(harness, "beta", now, 3);
        SeedTimes(harness, "alpha", now, 2);
        SeedTimes(harness, "zeta", now, 2);

        // Act
        var envelope = await harness.TopSkills.InvokeAsync();

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(["beta", "alpha", "zeta"], envelope.Results.Cast<TopSkillRow>().Select(row => row.Skill));
    }

    [Fact]
    public async Task InvokeAsync_DaysBackWindow_KeepsRecentDropsOld()
    {
        // Arrange
        using var harness = new SkillStatsHarness();
        harness.Migrate();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        harness.Seed("recent", now - ((5 * SecondsPerDay) - 60));
        harness.Seed("old", now - (6 * SecondsPerDay));

        // Act
        var envelope = await harness.TopSkills.InvokeAsync(days_back: 5);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(["recent"], envelope.Results.Cast<TopSkillRow>().Select(row => row.Skill));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(9999)]
    public async Task InvokeAsync_InvalidDaysBack_ReturnsInvalidArgument(int daysBack)
    {
        // Arrange
        using var harness = new SkillStatsHarness();
        harness.Migrate();

        // Act
        var envelope = await harness.TopSkills.InvokeAsync(days_back: daysBack);

        // Assert
        Assert.Equal(ErrorCodes.InvalidArgument, envelope.Error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task InvokeAsync_InvalidLimit_ReturnsInvalidArgument(int limit)
    {
        // Arrange
        using var harness = new SkillStatsHarness();
        harness.Migrate();

        // Act
        var envelope = await harness.TopSkills.InvokeAsync(limit: limit);

        // Assert
        Assert.Equal(ErrorCodes.InvalidArgument, envelope.Error.Code);
    }

    [Fact]
    public async Task InvokeAsync_EmptyStore_ReturnsEmptyResults()
    {
        // Arrange
        using var harness = new SkillStatsHarness();
        harness.Migrate();

        // Act
        var envelope = await harness.TopSkills.InvokeAsync();

        // Assert
        Assert.Null(envelope.Error);
        Assert.Empty(envelope.Results);
    }

    [Fact]
    public async Task InvokeAsync_FormatsTimestampsAsUtc()
    {
        // Arrange
        using var harness = new SkillStatsHarness();
        harness.Migrate();
        harness.Seed("csharp", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        // Act
        var envelope = await harness.TopSkills.InvokeAsync();

        // Assert
        var row = Assert.Single(envelope.Results.Cast<TopSkillRow>());
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$", row.FirstUsed);
    }

    private static void SeedTimes(SkillStatsHarness harness, string skill, long now, int times)
    {
        for (var index = 0; index < times; index++)
        {
            harness.Seed(skill, now - index);
        }
    }
}