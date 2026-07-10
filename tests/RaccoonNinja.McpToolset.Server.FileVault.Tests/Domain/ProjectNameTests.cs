using RaccoonNinja.McpToolset.Server.FileVault.Domain;
using RaccoonNinja.McpToolset.Server.FileVault.Errors;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tests.Domain;

/// <summary>Covers <see cref="ProjectName.Parse"/>: multi-segment validation, per-segment rules, and the length caps.</summary>
public class ProjectNameTests
{
    [Theory]
    [InlineData("vault-mcp")]
    [InlineData("monorepo/api")]
    [InlineData("a.b_c-d/e0/F9")]
    [InlineData("a/b/c/d/e/f/g/h")]
    public void Parse_ValidProject_ReturnsCanonicalValue(string value)
    {
        // Act
        var project = ProjectName.Parse(value);

        // Assert
        Assert.Equal(value, project.Value);
    }

    [Fact]
    public void Parse_MultiSegmentProject_ExposesSegmentsInOrder()
    {
        // Arrange
        var project = ProjectName.Parse("monorepo/api/v2");

        // Act
        var segments = project.Segments;

        // Assert
        Assert.Equal(["monorepo", "api", "v2"], segments);
    }

    [Fact]
    public void Parse_ValidProject_ToStringReturnsValue()
    {
        // Arrange
        var project = ProjectName.Parse("monorepo/api");

        // Act
        var text = project.ToString();

        // Assert
        Assert.Equal("monorepo/api", text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Parse_EmptyProject_ThrowsInvalidName(string value)
    {
        // Act
        var act = () => ProjectName.Parse(value);

        // Assert
        var exception = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.InvalidName, exception.Code);
    }

    [Theory]
    [InlineData("a//b")]
    [InlineData("a/../b")]
    [InlineData("./a")]
    [InlineData("a/./b")]
    [InlineData("a/")]
    [InlineData("/a")]
    [InlineData("a b/c")]
    [InlineData("a\\b/c")]
    [InlineData("src/CON")]
    [InlineData("docs/com3.md")]
    [InlineData("docs/note.")]
    public void Parse_InvalidSegment_ThrowsInvalidName(string value)
    {
        // Act
        var act = () => ProjectName.Parse(value);

        // Assert
        var exception = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.InvalidName, exception.Code);
    }

    [Fact]
    public void Parse_MoreThanMaxSegments_ThrowsInvalidName()
    {
        // Arrange
        const string value = "a/b/c/d/e/f/g/h/i";

        // Act
        var act = () => ProjectName.Parse(value);

        // Assert
        var exception = Assert.Throws<VaultException>(act);
        Assert.Contains("8 segments", exception.Message);
        Assert.Equal(VaultErrorCode.InvalidName, exception.Code);
    }

    [Fact]
    public void Parse_ProjectOfExactlyMaxLength_Succeeds()
    {
        // Arrange
        var value = new string('a', 512);

        // Act
        var project = ProjectName.Parse(value);

        // Assert
        Assert.Equal(512, project.Value.Length);
    }

    [Fact]
    public void Parse_ProjectLongerThanMaxLength_ThrowsInvalidName()
    {
        // Arrange
        var value = new string('a', 513);

        // Act
        var act = () => ProjectName.Parse(value);

        // Assert
        var exception = Assert.Throws<VaultException>(act);
        Assert.Contains("512", exception.Message);
        Assert.Equal(VaultErrorCode.InvalidName, exception.Code);
    }
}