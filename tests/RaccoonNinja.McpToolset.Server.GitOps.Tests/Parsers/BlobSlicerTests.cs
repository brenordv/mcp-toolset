using System.Text;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Parsers;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Parsers;

public sealed class BlobSlicerTests
{
    private const string Digits = "0123456789";
    private const string FourLines = "a\nb\nc\nd\n";
    private const string TwoLinesWithoutFinalNewline = "a\nb";

    [Theory]
    [InlineData(1, null, null, null, "line_range")]
    [InlineData(null, 1, null, null, "line_range")]
    [InlineData(0, 1, null, null, "line_range")]
    [InlineData(3, 2, null, null, "line_range")]
    [InlineData(null, null, -1, null, "byte_offset")]
    [InlineData(null, null, null, 0, "max_bytes")]
    public void ValidateArguments_MalformedShape_IsRejectedWithParam(int? lineStart, int? lineEnd, int? byteOffset, int? maxBytes, string param)
    {
        // Act
        var act = () => BlobSlicer.ValidateArguments(lineStart, lineEnd, byteOffset, maxBytes);

        // Assert
        var ex = Assert.Throws<RejectedArgumentException>(act);
        Assert.Equal(param, ex.Detail["param"]);
    }

    [Theory]
    [InlineData(null, null, null, null)]
    [InlineData(1, 1, 0, 1)]
    public void ValidateArguments_WellFormedShape_DoesNotThrow(int? lineStart, int? lineEnd, int? byteOffset, int? maxBytes)
    {
        // Act
        var ex = Record.Exception(() => BlobSlicer.ValidateArguments(lineStart, lineEnd, byteOffset, maxBytes));

        // Assert
        Assert.Null(ex);
    }

    [Fact]
    public void Slice_NoRangeNoCap_ReturnsWholeContentAndLineCount()
    {
        // Arrange
        var content = Utf8("a\nb\nc\n");

        // Act
        var slice = BlobSlicer.Slice(content, null, null, null, null);

        // Assert
        Assert.Equal(3, slice.TotalLines);
        Assert.Equal(content, slice.Bytes);
        Assert.False(slice.Truncated);
        Assert.Null(slice.NextByteOffset);
    }

    [Fact]
    public void Slice_ContentWithoutTrailingNewline_CountsLastLine()
    {
        // Arrange
        var content = Utf8(TwoLinesWithoutFinalNewline);

        // Act
        var slice = BlobSlicer.Slice(content, null, null, null, null);

        // Assert
        Assert.Equal(2, slice.TotalLines);
    }

    [Fact]
    public void Slice_EmptyContent_HasZeroLines()
    {
        // Arrange
        var content = Utf8(string.Empty);

        // Act
        var slice = BlobSlicer.Slice(content, null, null, null, null);

        // Assert
        Assert.Equal(0, slice.TotalLines);
        Assert.Empty(slice.Bytes);
    }

    [Fact]
    public void Slice_Range_ReturnsThoseLinesWithTerminators()
    {
        // Arrange
        var content = Utf8(FourLines);

        // Act
        var slice = BlobSlicer.Slice(content, 2, 3, null, null);

        // Assert
        Assert.Equal("b\nc\n", Text(slice));
        Assert.Equal(4, slice.TotalLines);
    }

    [Fact]
    public void Slice_RangeEndPastLastLine_IsClamped()
    {
        // Arrange
        var content = Utf8(FourLines);

        // Act
        var slice = BlobSlicer.Slice(content, 3, 99, null, null);

        // Assert
        Assert.Equal("c\nd\n", Text(slice));
    }

    [Fact]
    public void Slice_RangeToLastLineWithoutNewline_IncludesIt()
    {
        // Arrange
        var content = Utf8(TwoLinesWithoutFinalNewline);

        // Act
        var slice = BlobSlicer.Slice(content, 2, 2, null, null);

        // Assert
        Assert.Equal("b", Text(slice));
    }

    [Fact]
    public void Slice_HalfOrInvalidRange_IsRejected()
    {
        // Arrange
        var content = Utf8(FourLines);

        // Act
        var act = () => BlobSlicer.Slice(content, 1, null, null, null);

        // Assert
        var ex = Assert.Throws<RejectedArgumentException>(act);
        Assert.Equal("line_range", ex.Detail["param"]);
    }

    [Fact]
    public void Slice_RangeStartPastLastLine_IsRejectedWithTotalLines()
    {
        // Arrange
        var content = Utf8(FourLines);

        // Act
        var act = () => BlobSlicer.Slice(content, 5, 6, null, null);

        // Assert
        var ex = Assert.Throws<RejectedArgumentException>(act);
        Assert.Equal("line_range", ex.Detail["param"]);
        Assert.Equal(4, (int)ex.Detail["total_lines"]);
    }

    [Fact]
    public void Slice_RangeOnEmptyContent_IsRejected()
    {
        // Arrange
        var content = Utf8(string.Empty);

        // Act
        var act = () => BlobSlicer.Slice(content, 1, 1, null, null);

        // Assert
        var ex = Assert.Throws<RejectedArgumentException>(act);
        Assert.Equal("line_range", ex.Detail["param"]);
        Assert.Equal(0, (int)ex.Detail["total_lines"]);
    }

    [Fact]
    public void Slice_MaxBytes_CutsFlagsAndReportsNextOffset()
    {
        // Arrange
        var content = Utf8(Digits);

        // Act
        var slice = BlobSlicer.Slice(content, null, null, null, 4);

        // Assert
        Assert.Equal("0123", Text(slice));
        Assert.True(slice.Truncated);
        Assert.Equal(4, slice.NextByteOffset);
    }

    [Fact]
    public void Slice_MaxBytes_DoesNotSplitMultiByteCharacter()
    {
        // Arrange
        var content = AccentedWord;

        // Act
        var slice = BlobSlicer.Slice(content, null, null, null, 2);

        // Assert
        Assert.Equal("a", Text(slice));
        Assert.True(slice.Truncated);
        Assert.Equal(1, slice.NextByteOffset);
    }

    [Fact]
    public void Slice_MaxBytesBackoff_StopsAfterThreeContinuationBytes()
    {
        // Arrange
        var content = ContinuationRun;
        byte[] expected = [0x61, 0x80];

        // Act
        var slice = BlobSlicer.Slice(content, null, null, null, 5);

        // Assert
        Assert.Equal(expected, slice.Bytes);
        Assert.True(slice.Truncated);
        Assert.Equal(2, slice.NextByteOffset);
    }

    [Fact]
    public void Slice_MaxBytesSmallerThanFirstCharacter_KeepsOneByteAndAdvances()
    {
        // Arrange
        byte[] content = [0xC3, 0xA9, 0x62];
        byte[] expected = [0xC3];

        // Act
        var slice = BlobSlicer.Slice(content, null, null, null, 1);

        // Assert
        Assert.Equal(expected, slice.Bytes);
        Assert.True(slice.Truncated);
        Assert.Equal(1, slice.NextByteOffset);
    }

    [Fact]
    public void Slice_MaxBytesLargerThanContent_IsNotTruncated()
    {
        // Arrange
        var content = Utf8(Digits);

        // Act
        var slice = BlobSlicer.Slice(content, null, null, null, 100);

        // Assert
        Assert.Equal(Digits, Text(slice));
        Assert.False(slice.Truncated);
        Assert.Null(slice.NextByteOffset);
    }

    [Fact]
    public void Slice_RangeThenCap_AppliesCapToTheSlice()
    {
        // Arrange
        var content = Utf8("a\nbcd\nef\n");

        // Act
        var slice = BlobSlicer.Slice(content, 2, 3, null, 2);

        // Assert
        Assert.Equal("bc", Text(slice));
        Assert.True(slice.Truncated);
        Assert.Equal(2, slice.NextByteOffset);
    }

    [Fact]
    public void Slice_ByteOffset_SkipsThatManyBytes()
    {
        // Arrange
        var content = Utf8(Digits);

        // Act
        var slice = BlobSlicer.Slice(content, null, null, 4, null);

        // Assert
        Assert.Equal("456789", Text(slice));
        Assert.False(slice.Truncated);
        Assert.Null(slice.NextByteOffset);
    }

    [Fact]
    public void Slice_ByteOffsetThenCap_ReportsNextOffsetInTheSameFrame()
    {
        // Arrange
        var content = Utf8(Digits);

        // Act
        var slice = BlobSlicer.Slice(content, null, null, 4, 2);

        // Assert
        Assert.Equal("45", Text(slice));
        Assert.True(slice.Truncated);
        Assert.Equal(6, slice.NextByteOffset);
    }

    [Fact]
    public void Slice_ByteOffsetInsideMultiByteCharacter_MovesToNextCharacter()
    {
        // Arrange
        var content = AccentedWord;

        // Act
        var slice = BlobSlicer.Slice(content, null, null, 2, null);

        // Assert
        Assert.Equal("b", Text(slice));
    }

    [Fact]
    public void Slice_ByteOffsetMove_StopsAfterThreeContinuationBytes()
    {
        // Arrange
        var content = ContinuationRun;
        byte[] expected = [0x80, 0x80];

        // Act
        var slice = BlobSlicer.Slice(content, null, null, 1, null);

        // Assert
        Assert.Equal(expected, slice.Bytes);
    }

    [Fact]
    public void Slice_ByteOffsetInsideLastCharacter_ReturnsEmptyContent()
    {
        // Arrange
        byte[] content = [0x61, 0xC3, 0xA9];

        // Act
        var slice = BlobSlicer.Slice(content, null, null, 2, null);

        // Assert
        Assert.Empty(slice.Bytes);
        Assert.False(slice.Truncated);
        Assert.Null(slice.NextByteOffset);
    }

    [Fact]
    public void Slice_ContentStartingWithContinuationByte_IsReturnedWhole()
    {
        // Arrange
        byte[] content = [0x80, 0x61];

        // Act
        var slice = BlobSlicer.Slice(content, null, null, null, null);

        // Assert
        Assert.Equal(content, slice.Bytes);
    }

    [Fact]
    public void Slice_ByteOffsetAtEnd_IsRejectedWithContentBytes()
    {
        // Arrange
        var content = Utf8("0123");

        // Act
        var act = () => BlobSlicer.Slice(content, null, null, 4, null);

        // Assert
        var ex = Assert.Throws<RejectedArgumentException>(act);
        Assert.Equal("byte_offset", ex.Detail["param"]);
        Assert.Equal(4, (int)ex.Detail["content_bytes"]);
    }

    [Fact]
    public void Slice_ByteOffsetZeroOnEmptyContent_ReturnsEmpty()
    {
        // Arrange
        var content = Utf8(string.Empty);

        // Act
        var slice = BlobSlicer.Slice(content, null, null, 0, null);

        // Assert
        Assert.Empty(slice.Bytes);
        Assert.False(slice.Truncated);
    }

    [Fact]
    public void Slice_RangeThenOffset_AppliesOffsetWithinTheSlice()
    {
        // Arrange
        var content = Utf8("a\nbc\nd\n");

        // Act
        var slice = BlobSlicer.Slice(content, 2, 3, 1, null);

        // Assert
        Assert.Equal("c\nd\n", Text(slice));
    }

    #region Test Helpers

    private static byte[] AccentedWord => [0x61, 0xC3, 0xA9, 0x62];

    private static byte[] ContinuationRun => [0x61, 0x80, 0x80, 0x80, 0x80, 0x80];

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static string Text(BlobSlice slice) => Encoding.UTF8.GetString(slice.Bytes);

    #endregion
}