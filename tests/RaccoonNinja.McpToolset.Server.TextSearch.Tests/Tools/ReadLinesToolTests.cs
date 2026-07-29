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
        using var harness = new TextSearchHarness();
        harness.Write("a.txt", "l1\nl2\nl3\nl4\nl5");

        var envelope = await harness.ReadLines.InvokeAsync(path: "a.txt", start_line: 2, end_line: 4);

        Assert.Null(envelope.Error);
        var lines = envelope.Results.Cast<NumberedLine>().ToArray();
        Assert.Equal([2, 3, 4], lines.Select(line => line.Line));
        Assert.Equal(["l2", "l3", "l4"], lines.Select(line => line.Text));
    }

    [Fact]
    public async Task ReadLines_EndBeyondEof_ClampsToLastLine()
    {
        using var harness = new TextSearchHarness();
        harness.Write("a.txt", "l1\nl2\nl3");

        var envelope = await harness.ReadLines.InvokeAsync(path: "a.txt", start_line: 2, end_line: 100);

        var lines = envelope.Results.Cast<NumberedLine>().ToArray();
        Assert.Equal([2, 3], lines.Select(line => line.Line));
        Assert.False(envelope.Truncated);
    }

    [Fact]
    public async Task ReadLines_StartBeyondEof_ReturnsEmpty()
    {
        using var harness = new TextSearchHarness();
        harness.Write("a.txt", "l1\nl2");

        var envelope = await harness.ReadLines.InvokeAsync(path: "a.txt", start_line: 100);

        Assert.Null(envelope.Error);
        Assert.Empty(envelope.Results);
    }

    [Fact]
    public async Task ReadLines_BinaryFile_IsRefused()
    {
        using var harness = new TextSearchHarness();
        harness.WriteBytes("blob.bin", [0x00, 0x01, 0x02]);

        var envelope = await harness.ReadLines.InvokeAsync(path: "blob.bin");

        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.IsBinary, envelope.Error.Code);
    }

    [Fact]
    public async Task ReadLines_Denylisted_ReportsNotFoundNotDenied()
    {
        using var harness = new TextSearchHarness();
        harness.Write(".env", "SECRET=1");

        var envelope = await harness.ReadLines.InvokeAsync(path: ".env");

        // Reported as not-found so a single-path read is not an existence oracle for a secret.
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.NotFound, envelope.Error.Code);
    }

    [Fact]
    public async Task ReadLines_SpanCap_LimitsReturnedLinesAndMarksTruncated()
    {
        using var harness = new TextSearchHarness();
        var lineCount = SearchConfig.DefaultMaxLineSpan + 1000;
        harness.Write("big.txt", string.Join('\n', Enumerable.Range(1, lineCount).Select(i => $"l{i}")));

        // end_line 0 asks for a full span from the start; the span cap must bound it.
        var envelope = await harness.ReadLines.InvokeAsync(path: "big.txt", start_line: 1, end_line: 0);

        Assert.Null(envelope.Error);
        Assert.Equal(SearchConfig.DefaultMaxLineSpan, envelope.Results.Count);
        Assert.True(envelope.Truncated);
    }
}