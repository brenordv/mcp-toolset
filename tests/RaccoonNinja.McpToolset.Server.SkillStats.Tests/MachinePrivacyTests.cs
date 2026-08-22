using System.Text.Json;
using RaccoonNinja.McpToolset.Server.SkillStats.Errors;
using RaccoonNinja.McpToolset.Server.SkillStats.Tests.TestSupport;

namespace RaccoonNinja.McpToolset.Server.SkillStats.Tests;

public sealed class MachinePrivacyTests
{
    [Fact]
    public async Task StoreUnavailableError_MessageDoesNotLeakStorePath()
    {
        // Arrange
        using var harness = new SkillStatsHarness();

        // Act
        var envelope = await harness.TopSkills.InvokeAsync();

        // Assert
        Assert.Equal(ErrorCodes.StoreUnavailable, envelope.Error.Code);
        Assert.DoesNotContain(harness.Home, envelope.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FiltersApplied_DoNotLeakStorePath()
    {
        // Arrange
        using var harness = new SkillStatsHarness();
        harness.Migrate();
        harness.Seed("csharp", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        // Act
        var envelope = await harness.SkillUsage.InvokeAsync("csharp", days_back: 5, limit: 10);
        var serialized = JsonSerializer.Serialize(envelope.FiltersApplied);

        // Assert
        Assert.DoesNotContain(harness.Home, serialized, StringComparison.OrdinalIgnoreCase);
    }
}