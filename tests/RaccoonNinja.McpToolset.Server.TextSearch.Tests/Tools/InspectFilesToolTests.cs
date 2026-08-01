using System.Text;
using RaccoonNinja.McpToolset.Server.TextSearch.Models;
using RaccoonNinja.McpToolset.Server.TextSearch.Tests.TestSupport;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tests.Tools;

public sealed class InspectFilesToolTests
{
    [Fact]
    public async Task Inspect_Utf8_ReportsLineShape()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("a.txt", "one\ntwo  \nthree");

        // Act
        var inspection = await InspectSingle(harness, "a.txt");

        // Assert
        Assert.Equal("utf-8", inspection.Encoding);
        Assert.False(inspection.IsBinary);
        Assert.False(inspection.HasBom);
        Assert.Equal("lf", inspection.LineEndings);
        Assert.False(inspection.FinalNewline);
        Assert.Equal(3, inspection.LineCount);
        Assert.Equal(1, inspection.TrailingWhitespaceLines);
    }

    [Fact]
    public async Task Inspect_BomlessUtf16Ascii_DetectsUtf16NotUtf8()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.WriteBytes("wide.txt", Encoding.Unicode.GetBytes("hello\nworld"));

        // Act
        var inspection = await InspectSingle(harness, "wide.txt");

        // Assert
        Assert.Contains("utf-16", inspection.Encoding, StringComparison.Ordinal);
        Assert.False(inspection.IsBinary);
        Assert.Equal(2, inspection.LineCount);
    }

    [Fact]
    public async Task Inspect_Utf8Bom_StripsBomFromLineOne()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("abc\n")).ToArray();
        harness.WriteBytes("bom.txt", bytes);

        // Act
        var inspection = await InspectSingle(harness, "bom.txt");

        // Assert
        Assert.Equal("utf-8", inspection.Encoding);
        Assert.True(inspection.HasBom);
        Assert.Equal(1, inspection.LineCount);
        Assert.True(inspection.FinalNewline);
    }

    [Fact]
    public async Task Inspect_MixedLineEndings_ReportsMixed()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("mix.txt", "a\r\nb\nc");

        // Act
        var inspection = await InspectSingle(harness, "mix.txt");

        // Assert
        Assert.Equal("mixed", inspection.LineEndings);
        Assert.Equal(3, inspection.LineCount);
    }

    [Fact]
    public async Task Inspect_BinaryFile_IsBinaryTrue()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.WriteBytes("blob.png", [0x89, 0x50, 0x4E, 0x47, 0x00, 0x1A, 0x0A]);

        // Act
        var inspection = await InspectSingle(harness, "blob.png");

        // Assert
        Assert.True(inspection.IsBinary);
        Assert.Equal(0, inspection.LineCount);
    }

    private static async Task<FileInspection> InspectSingle(TextSearchHarness harness, string path)
    {
        var envelope = await harness.Inspect.InvokeAsync(paths: [path]);
        Assert.Null(envelope.Error);
        return Assert.IsType<FileInspection>(Assert.Single(envelope.Results));
    }
}