using RaccoonNinja.McpToolset.Server.TextEdit.Models;
using RaccoonNinja.McpToolset.Server.TextEdit.Tests.TestSupport;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Tests.Tools;

public sealed class ReplaceTextToolTests
{
    [Fact]
    public async Task InvokeAsync_SweepReachingIgnoreFile_RefusesItAndChangesTheRest()
    {
        // Arrange
        using var harness = new TextEditHarness();
        harness.WriteText(".gitignore", "hello world\n");
        harness.WriteText("a.txt", "hello world");

        // Act
        var envelope = await harness.Replace.InvokeAsync("world", "text");

        // Assert
        Assert.Null(envelope.Error);
        var result = Assert.IsType<MutationResult>(Assert.Single(envelope.Results));
        Assert.Equal(1, result.Changed);
        var refused = Assert.Single(result.Files, file => file.Path == ".gitignore");
        Assert.Equal("ignore_file", refused.RefusalReason);
        Assert.Equal("hello world\n", harness.ReadText(".gitignore"));
        Assert.Equal("hello text", harness.ReadText("a.txt"));
    }

    [Fact]
    public async Task InvokeAsync_MoreFilesThanMaxFiles_ReportsTruncated()
    {
        // Arrange
        const string original = "hello world";
        const string pastCapFile = "c.txt";
        const int maxFiles = 2;
        using var harness = new TextEditHarness();
        harness.WriteText("a.txt", original);
        harness.WriteText("b.txt", original);
        harness.WriteText(pastCapFile, original);

        // Act
        var envelope = await harness.Replace.InvokeAsync("world", "text", glob: "*.txt", max_files: maxFiles);

        // Assert
        Assert.Null(envelope.Error);
        Assert.True(envelope.Truncated);
        var result = Assert.IsType<MutationResult>(Assert.Single(envelope.Results));
        Assert.Equal(maxFiles, result.Attempted);
        Assert.Equal(original, harness.ReadText(pastCapFile));
        Assert.Equal(1L, (long)harness.Metrics.Summary()["truncations_total"]);
    }

    [Fact]
    public async Task InvokeAsync_SelectionUnderMaxFiles_ReportsSkippedSymlinksAndNotTruncated()
    {
        // Arrange
        using var harness = new TextEditHarness();
        harness.WriteText("a.txt", "hello world");

        // Act
        var envelope = await harness.Replace.InvokeAsync("world", "text", glob: "*.txt", max_files: 2);

        // Assert
        Assert.Null(envelope.Error);
        Assert.False(envelope.Truncated);
        Assert.Equal(0, envelope.SkippedSymlinks);
        Assert.Equal(0L, (long)harness.Metrics.Summary()["truncations_total"]);
    }
}