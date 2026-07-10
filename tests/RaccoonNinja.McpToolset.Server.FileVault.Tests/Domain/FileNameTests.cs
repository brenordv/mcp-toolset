using RaccoonNinja.McpToolset.Server.FileVault.Domain;
using RaccoonNinja.McpToolset.Server.FileVault.Errors;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tests.Domain;

/// <summary>Covers <see cref="FileName.Parse"/>: the single-segment contract, the Windows-hardening rules, and the length cap.</summary>
public class FileNameTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("notes")]
    [InlineData("report-2026_v1.md")]
    [InlineData(".gitignore")]
    [InlineData("CONSOLE")]
    [InlineData("COM0")]
    [InlineData("COM10")]
    [InlineData("NULL")]
    public void Parse_ValidName_ReturnsValue(string value)
    {
        // Act
        var name = FileName.Parse(value);

        // Assert
        Assert.Equal(value, name.Value);
    }

    [Fact]
    public void Parse_ValidName_ToStringReturnsValue()
    {
        // Arrange
        var name = FileName.Parse("notes.md");

        // Act
        var text = name.ToString();

        // Assert
        Assert.Equal("notes.md", text);
    }

    [Fact]
    public void Parse_SameName_ProducesEqualRecords()
    {
        // Act
        var first = FileName.Parse("notes.md");
        var second = FileName.Parse("notes.md");

        // Assert
        Assert.Equal(second, first);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a b")]
    [InlineData("café")]
    [InlineData("a\\b")]
    [InlineData("a:b")]
    [InlineData("a/b")]
    public void Parse_EmptyTraversalOrCharsetViolation_ThrowsInvalidName(string value)
    {
        // Act
        var act = () => FileName.Parse(value);

        // Assert
        var exception = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.InvalidName, exception.Code);
    }

    [Theory]
    [InlineData("note.")]
    [InlineData("nul")]
    [InlineData("NUL.txt")]
    [InlineData("com3")]
    [InlineData("Com3.md")]
    [InlineData("CON")]
    [InlineData("lpt9")]
    public void Parse_WindowsHostileName_ThrowsInvalidName(string value)
    {
        // Act
        var act = () => FileName.Parse(value);

        // Assert
        var exception = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.InvalidName, exception.Code);
    }

    [Fact]
    public void Parse_NameOfExactlyMaxLength_Succeeds()
    {
        // Arrange
        var value = new string('a', 128);

        // Act
        var name = FileName.Parse(value);

        // Assert
        Assert.Equal(128, name.Value.Length);
    }

    [Fact]
    public void Parse_NameLongerThanMaxLength_ThrowsInvalidName()
    {
        // Arrange
        var value = new string('a', 129);

        // Act
        var act = () => FileName.Parse(value);

        // Assert
        var exception = Assert.Throws<VaultException>(act);
        Assert.Contains("128", exception.Message);
        Assert.Equal(VaultErrorCode.InvalidName, exception.Code);
    }
}