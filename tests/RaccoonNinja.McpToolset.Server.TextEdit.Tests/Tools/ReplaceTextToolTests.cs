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
}