using RaccoonNinja.McpToolset.Shared.SkillStats.Domain;

namespace RaccoonNinja.McpToolset.Shared.SkillStats.Tests.Domain;

public sealed class SkillNameTests
{
    [Theory]
    [InlineData("/csharp", "csharp")]
    [InlineData("csharp", "csharp")]
    [InlineData("//x", "x")]
    [InlineData("  /brain/thing \n", "brain/thing")]
    [InlineData("plugin:skill", "plugin:skill")]
    [InlineData("CSharp", "CSharp")]
    public void TryNormalize_UsableName_StripsLeadingSlashesAndPreservesRest(string raw, string expected)
    {
        // Act
        var ok = SkillName.TryNormalize(raw, out var normalized);

        // Assert
        Assert.True(ok);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("///")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryNormalize_NoUsableName_ReturnsFalse(string raw)
    {
        // Act
        var ok = SkillName.TryNormalize(raw, out var normalized);

        // Assert
        Assert.False(ok);
        Assert.Null(normalized);
    }
}