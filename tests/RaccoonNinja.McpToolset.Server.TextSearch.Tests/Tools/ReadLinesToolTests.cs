using RaccoonNinja.McpToolset.Server.TextSearch.Configuration;
using RaccoonNinja.McpToolset.Server.TextSearch.Errors;
using RaccoonNinja.McpToolset.Server.TextSearch.Models;
using RaccoonNinja.McpToolset.Server.TextSearch.Tests.TestSupport;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tests.Tools;

public sealed class ReadLinesToolTests
{
    [Fact]
    public async Task ReadLines_ReturnsRequestedSlice()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("a.txt", "l1\nl2\nl3\nl4\nl5");

        // Act
        var envelope = await harness.ReadLines.InvokeAsync(path: "a.txt", start_line: 2, end_line: 4);

        // Assert
        Assert.Null(envelope.Error);
        var lines = envelope.Results.Cast<NumberedLine>().ToArray();
        Assert.Equal([2, 3, 4], lines.Select(line => line.Line));
        Assert.Equal(["l2", "l3", "l4"], lines.Select(line => line.Text));
    }

    [Fact]
    public async Task ReadLines_EndBeyondEof_ClampsToLastLine()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("a.txt", "l1\nl2\nl3");

        // Act
        var envelope = await harness.ReadLines.InvokeAsync(path: "a.txt", start_line: 2, end_line: 100);

        // Assert
        var lines = envelope.Results.Cast<NumberedLine>().ToArray();
        Assert.Equal([2, 3], lines.Select(line => line.Line));
        Assert.False(envelope.Truncated);
    }

    [Fact]
    public async Task ReadLines_StartBeyondEof_ReturnsEmpty()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("a.txt", "l1\nl2");

        // Act
        var envelope = await harness.ReadLines.InvokeAsync(path: "a.txt", start_line: 100);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Empty(envelope.Results);
    }

    [Fact]
    public async Task ReadLines_BinaryFile_IsRefused()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.WriteBytes("blob.bin", [0x00, 0x01, 0x02]);

        // Act
        var envelope = await harness.ReadLines.InvokeAsync(path: "blob.bin");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.IsBinary, envelope.Error.Code);
    }

    [Fact]
    public async Task ReadLines_Denylisted_ReportsNotFoundNotDenied()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write(".env", "SECRET=1");

        // Act
        var envelope = await harness.ReadLines.InvokeAsync(path: ".env");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.NotFound, envelope.Error.Code);
    }

    [Fact]
    public async Task ReadLines_SpanCap_LimitsReturnedLinesAndMarksTruncated()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        var lineCount = SearchConfig.DefaultMaxLineSpan + 1000;
        harness.Write("big.txt", string.Join('\n', Enumerable.Range(1, lineCount).Select(i => $"l{i}")));

        // Act
        var envelope = await harness.ReadLines.InvokeAsync(path: "big.txt", start_line: 1, end_line: 0);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(SearchConfig.DefaultMaxLineSpan, envelope.Results.Count);
        Assert.True(envelope.Truncated);
    }
}