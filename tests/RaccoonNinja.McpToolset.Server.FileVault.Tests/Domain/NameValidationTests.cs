using RaccoonNinja.McpToolset.Server.FileVault.Domain;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tests.Domain;

/// <summary>Covers the shared segment rules: charset, traversal, trailing dots, and reserved device names.</summary>
public class NameValidationTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("notes")]
    [InlineData("report-2026_v1.md")]
    [InlineData("A-Za-z0-9._-")]
    [InlineData(".gitignore")]
    [InlineData("archive.tar.gz")]
    public void IsValidSegment_CharsetCompliantSegment_ReturnsTrue(string segment)
    {
        // Act
        var result = NameValidation.IsValidSegment(segment);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    public void IsValidSegment_EmptyOrTraversalSegment_ReturnsFalse(string segment)
    {
        // Act
        var result = NameValidation.IsValidSegment(segment);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("a b")]
    [InlineData("café")]
    [InlineData("a\\b")]
    [InlineData("a:b")]
    [InlineData("a/b")]
    [InlineData("a*b")]
    [InlineData("a?b")]
    public void IsValidSegment_CharOutsideCharset_ReturnsFalse(string segment)
    {
        // Act
        var result = NameValidation.IsValidSegment(segment);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("note.")]
    [InlineData("...")]
    [InlineData("a.b.")]
    public void IsValidSegment_TrailingDot_ReturnsFalse(string segment)
    {
        // Act
        var result = NameValidation.IsValidSegment(segment);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    [InlineData("nul")]
    [InlineData("NUL.txt")]
    [InlineData("nul.tar.gz")]
    [InlineData("COM1")]
    [InlineData("com3")]
    [InlineData("Com3.md")]
    [InlineData("COM9")]
    [InlineData("LPT1")]
    [InlineData("lpt5.log")]
    [InlineData("LPT9")]
    public void IsValidSegment_ReservedDeviceName_ReturnsFalse(string segment)
    {
        // Act
        var result = NameValidation.IsValidSegment(segment);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("CONSOLE")]
    [InlineData("COM0")]
    [InlineData("COM10")]
    [InlineData("LPT0")]
    [InlineData("LPT10")]
    [InlineData("NULL")]
    [InlineData("AUXILIARY")]
    public void IsValidSegment_ReservedLookalike_ReturnsTrue(string segment)
    {
        // Act
        var result = NameValidation.IsValidSegment(segment);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Caps_MatchTheDocumentedD4Contract()
    {
        // Assert
        Assert.Equal(128, NameValidation.MaxNameLength);
        Assert.Equal(512, NameValidation.MaxProjectLength);
        Assert.Equal(8, NameValidation.MaxProjectSegments);
    }
}