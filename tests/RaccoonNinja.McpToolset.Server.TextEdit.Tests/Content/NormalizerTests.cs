using RaccoonNinja.McpToolset.Server.TextEdit.Content;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Tests.Content;

public sealed class NormalizerTests
{
    [Fact]
    public void Transform_TrimTrailingWhitespace_RemovesSpacesAndTabsBeforeTerminators()
    {
        // Arrange
        var normalizer = new Normalizer(new NormalizeOptions { TrimTrailingWhitespace = true });

        // Act
        var result = normalizer.Transform("a  \nb\t\n");

        // Assert
        Assert.Equal("a\nb\n", result.NewText);
    }

    [Fact]
    public void Transform_LineEndingsLf_ConvertsEveryTerminatorToLf()
    {
        // Arrange
        var normalizer = new Normalizer(new NormalizeOptions { LineEndings = LineEndingMode.Lf });

        // Act
        var result = normalizer.Transform("a\r\nb\rc");

        // Assert
        Assert.Equal("a\nb\nc", result.NewText);
    }

    [Fact]
    public void Transform_LineEndingsCrlf_ConvertsEveryTerminatorToCrlf()
    {
        // Arrange
        var normalizer = new Normalizer(new NormalizeOptions { LineEndings = LineEndingMode.Crlf });

        // Act
        var result = normalizer.Transform("a\nb\n");

        // Assert
        Assert.Equal("a\r\nb\r\n", result.NewText);
    }

    [Fact]
    public void Transform_EnsureFinalNewline_AddsOneWhenMissing()
    {
        // Arrange
        var normalizer = new Normalizer(new NormalizeOptions { FinalNewline = FinalNewlineMode.Ensure });

        // Act
        var result = normalizer.Transform("abc");

        // Assert
        Assert.Equal("abc\n", result.NewText);
    }

    [Fact]
    public void Transform_TrimFinalNewline_RemovesTrailingTerminators()
    {
        // Arrange
        var normalizer = new Normalizer(new NormalizeOptions { FinalNewline = FinalNewlineMode.Trim });

        // Act
        var result = normalizer.Transform("abc\n\n");

        // Assert
        Assert.Equal("abc", result.NewText);
    }

    [Fact]
    public void Transform_BomStrip_ReportsBomOverrideFalse()
    {
        // Arrange
        var normalizer = new Normalizer(new NormalizeOptions { Bom = BomMode.Strip });

        // Act
        var result = normalizer.Transform("abc");

        // Assert
        Assert.False(result.BomOverride);
        Assert.Equal("abc", result.NewText);
    }

    [Fact]
    public void Transform_AllPreserve_LeavesTextAndBomUntouched()
    {
        // Arrange
        var normalizer = new Normalizer(new NormalizeOptions());

        // Act
        var result = normalizer.Transform("a \r\nb");

        // Assert
        Assert.Equal("a \r\nb", result.NewText);
        Assert.Null(result.BomOverride);
    }
}