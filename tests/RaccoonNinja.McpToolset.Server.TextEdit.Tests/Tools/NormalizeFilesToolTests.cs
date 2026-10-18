using RaccoonNinja.McpToolset.Server.TextEdit.Models;
using RaccoonNinja.McpToolset.Server.TextEdit.Tests.TestSupport;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Tests.Tools;

public sealed class NormalizeFilesToolTests
{
    [Fact]
    public async Task InvokeAsync_MoreFilesThanMaxFiles_ReportsTruncated()
    {
        // Arrange
        const string original = "x  \n";
        const string pastCapFile = "c.txt";
        const int maxFiles = 2;
        using var harness = new TextEditHarness();
        harness.WriteText("a.txt", original);
        harness.WriteText("b.txt", original);
        harness.WriteText(pastCapFile, original);

        // Act
        var envelope = await harness.Normalize.InvokeAsync(glob: "*.txt", max_files: maxFiles, trim_trailing_whitespace: true);

        // Assert
        Assert.Null(envelope.Error);
        Assert.True(envelope.Truncated);
        var result = Assert.IsType<MutationResult>(Assert.Single(envelope.Results));
        Assert.Equal(maxFiles, result.Attempted);
        Assert.Equal(original, harness.ReadText(pastCapFile));
    }
}