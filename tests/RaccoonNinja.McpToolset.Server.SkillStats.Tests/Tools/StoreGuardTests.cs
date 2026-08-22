using RaccoonNinja.McpToolset.Server.SkillStats.Errors;
using RaccoonNinja.McpToolset.Server.SkillStats.Tests.TestSupport;

namespace RaccoonNinja.McpToolset.Server.SkillStats.Tests.Tools;

public sealed class StoreGuardTests
{
    [Fact]
    public async Task Tools_MissingStore_AllReturnStoreUnavailable()
    {
        // Arrange
        using var harness = new SkillStatsHarness();

        // Act
        var top = await harness.TopSkills.InvokeAsync();
        var usage = await harness.SkillUsage.InvokeAsync("csharp");
        var summary = await harness.UsageSummary.InvokeAsync();

        // Assert
        Assert.Equal(ErrorCodes.StoreUnavailable, top.Error.Code);
        Assert.Equal(ErrorCodes.StoreUnavailable, usage.Error.Code);
        Assert.Equal(ErrorCodes.StoreUnavailable, summary.Error.Code);
    }

    [Fact]
    public async Task Tools_MissingStore_NeverCreateTheDatabase()
    {
        // Arrange
        using var harness = new SkillStatsHarness();

        // Act
        await harness.TopSkills.InvokeAsync();
        await harness.SkillUsage.InvokeAsync("csharp");
        await harness.UsageSummary.InvokeAsync();

        // Assert
        Assert.False(File.Exists(harness.Config.DbPath));
    }

    [Fact]
    public async Task Tools_NewerSchema_ReturnStoreUnavailable()
    {
        // Arrange
        using var harness = new SkillStatsHarness();
        harness.Migrate();
        harness.SetSchemaVersion(99);

        // Act
        var envelope = await harness.TopSkills.InvokeAsync();

        // Assert
        Assert.Equal(ErrorCodes.StoreUnavailable, envelope.Error.Code);
    }
}