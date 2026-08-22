using RaccoonNinja.McpToolset.Server.SkillStats.Errors;
using RaccoonNinja.McpToolset.Server.SkillStats.Models;
using RaccoonNinja.McpToolset.Server.SkillStats.Tests.TestSupport;

namespace RaccoonNinja.McpToolset.Server.SkillStats.Tests.Tools;

public sealed class SkillUsageToolTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/")]
    public async Task InvokeAsync_MissingSkill_ReturnsInvalidArgument(string skill)
    {
        // Arrange
        using var harness = new SkillStatsHarness();
        harness.Migrate();

        // Act
        var envelope = await harness.SkillUsage.InvokeAsync(skill);

        // Assert
        Assert.Equal(ErrorCodes.InvalidArgument, envelope.Error.Code);
    }

    [Fact]
    public async Task InvokeAsync_LeadingSlashSkill_FindsNormalizedSkill()
    {
        // Arrange
        using var harness = new SkillStatsHarness();
        harness.Migrate();
        harness.Seed("csharp", DateTimeOffset.UtcNow.ToUnixTimeSeconds(), args: "x");

        // Act
        var envelope = await harness.SkillUsage.InvokeAsync("/csharp");

        // Assert
        Assert.Null(envelope.Error);
        Assert.Single(envelope.Results);
    }

    [Fact]
    public async Task InvokeAsync_UnknownSkill_ReturnsEmptyResults()
    {
        // Arrange
        using var harness = new SkillStatsHarness();
        harness.Migrate();

        // Act
        var envelope = await harness.SkillUsage.InvokeAsync("nonexistent");

        // Assert
        Assert.Null(envelope.Error);
        Assert.Empty(envelope.Results);
    }

    [Fact]
    public async Task InvokeAsync_ReturnsNewestFirst()
    {
        // Arrange
        using var harness = new SkillStatsHarness();
        harness.Migrate();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        harness.Seed("csharp", now - 30, args: "old");
        harness.Seed("csharp", now - 10, args: "new");

        // Act
        var envelope = await harness.SkillUsage.InvokeAsync("csharp");

        // Assert
        Assert.Equal(["new", "old"], envelope.Results.Cast<SkillUsageRow>().Select(row => row.Args));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public async Task InvokeAsync_InvalidLimit_ReturnsInvalidArgument(int limit)
    {
        // Arrange
        using var harness = new SkillStatsHarness();
        harness.Migrate();

        // Act
        var envelope = await harness.SkillUsage.InvokeAsync("csharp", limit: limit);

        // Assert
        Assert.Equal(ErrorCodes.InvalidArgument, envelope.Error.Code);
    }
}