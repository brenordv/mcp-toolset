using System.Globalization;
using RaccoonNinja.McpToolset.Server.FileVault.Domain;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tests.Domain;

/// <summary>Covers <see cref="LineDiff"/>: LCS output shape, Rust-style line splitting, and the truncation trailer.</summary>
public class LineDiffTests
{
    [Fact]
    public void Compute_AdditionOnly_EmitsPlusLinesAfterCommonLines()
    {
        // Act
        var diff = LineDiff.Compute("a\nb", "a\nb\nc", 100);

        // Assert
        Assert.Equal(" a\n b\n+c", diff);
    }

    [Fact]
    public void Compute_RemovalOnly_EmitsMinusLineBetweenCommonLines()
    {
        // Act
        var diff = LineDiff.Compute("a\nb\nc", "a\nc", 100);

        // Assert
        Assert.Equal(" a\n-b\n c", diff);
    }

    [Fact]
    public void Compute_ChangedLine_EmitsMinusThenPlus()
    {
        // Act
        var diff = LineDiff.Compute("a\nold\nc", "a\nnew\nc", 100);

        // Assert
        Assert.Equal(" a\n-old\n+new\n c", diff);
    }

    [Fact]
    public void Compute_IdenticalInputs_EmitsOnlyCommonLines()
    {
        // Act
        var diff = LineDiff.Compute("a\nb", "a\nb", 100);

        // Assert
        Assert.Equal(" a\n b", diff);
    }

    [Fact]
    public void Compute_EmptyBase_EmitsAllLinesAsAdditions()
    {
        // Act
        var diff = LineDiff.Compute(string.Empty, "x\ny", 100);

        // Assert
        Assert.Equal("+x\n+y", diff);
    }

    [Fact]
    public void Compute_EmptyCurrent_EmitsAllLinesAsRemovals()
    {
        // Act
        var diff = LineDiff.Compute("x\ny", string.Empty, 100);

        // Assert
        Assert.Equal("-x\n-y", diff);
    }

    [Fact]
    public void Compute_BothInputsNull_ReturnsEmptyString()
    {
        // Act
        var diff = LineDiff.Compute(null, null, 100);

        // Assert
        Assert.Empty(diff);
    }

    [Fact]
    public void Compute_CrlfAndTrailingNewline_NormalizeToTheSameLines()
    {
        // Act
        var diff = LineDiff.Compute("a\r\nb\r\n", "a\nb", 100);

        // Assert
        Assert.Equal(" a\n b", diff);
    }

    [Fact]
    public void Compute_OutputExceedsMaxLines_TruncatesWithExactMarker()
    {
        // Arrange
        var current = "l1\nl2\nl3\nl4\nl5";

        // Act
        var diff = LineDiff.Compute(string.Empty, current, 3);

        // Assert
        Assert.Equal("+l1\n+l2\n+l3\n… (2 more diff lines omitted)", diff);
    }

    [Fact]
    public void Compute_OutputExactlyMaxLines_DoesNotTruncate()
    {
        // Arrange
        var current = "l1\nl2\nl3";

        // Act
        var diff = LineDiff.Compute(string.Empty, current, 3);

        // Assert
        Assert.Equal("+l1\n+l2\n+l3", diff);
    }

    [Fact]
    public void Compute_OutputBeyondTwoHundredLines_CapsAtTwoHundredWithMarker()
    {
        // Arrange
        var current = string.Join(
            '\n',
            Enumerable.Range(1, 250).Select(i => "line" + i.ToString(CultureInfo.InvariantCulture)));

        // Act
        var diff = LineDiff.Compute(string.Empty, current, 200);
        var lines = diff.Split('\n');

        // Assert
        Assert.Equal(201, lines.Length);
        Assert.All(lines[..200], line => Assert.True(line.StartsWith('+')));
        Assert.Equal("… (50 more diff lines omitted)", lines[200]);
    }

    [Fact]
    public void Compute_MultiByteContent_DiffsWholeLines()
    {
        // Arrange
        const string baseText = "こんにちは\n😀 old";
        const string currentText = "こんにちは\n🎉 new";

        // Act
        var diff = LineDiff.Compute(baseText, currentText, 100);

        // Assert
        Assert.Equal(" こんにちは\n-😀 old\n+🎉 new", diff);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("a", 1)]
    [InlineData("a\n", 1)]
    [InlineData("\n", 1)]
    [InlineData("a\nb", 2)]
    [InlineData("a\r\nb\r\n", 2)]
    public void CountLines_Input_MatchesRustLinesSemantics(string text, long expected)
    {
        // Act
        var count = LineDiff.CountLines(text);

        // Assert
        Assert.Equal(expected, count);
    }
}