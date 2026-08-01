using RaccoonNinja.McpToolset.Server.TextSearch.Errors;
using RaccoonNinja.McpToolset.Server.TextSearch.Tests.TestSupport;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tests.Tools;

public sealed class FindFilesToolTests
{
    [Fact]
    public async Task FindFiles_GlobMode_ReturnsMatchingFiles()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("src/a.cs", "1");
        harness.Write("src/b.txt", "2");
        harness.Write("test/c.cs", "3");

        // Act
        var envelope = await harness.Find.InvokeAsync(glob: "**/*.cs");

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(["src/a.cs", "test/c.cs"], TextSearchHarness.Paths(envelope));
    }

    [Fact]
    public async Task FindFiles_EmptySelector_ReturnsEverythingPruned()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write(".gitignore", "*.log\n");
        harness.Write("keep.cs", "1");
        harness.Write("skip.log", "2");
        harness.Write(".env", "SECRET=1");

        // Act
        var envelope = await harness.Find.InvokeAsync();

        // Assert
        Assert.Equal([".gitignore", "keep.cs"], TextSearchHarness.Paths(envelope));
    }

    [Fact]
    public async Task FindFiles_PathsMode_OmitsDenylisted()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("ok.cs", "1");
        harness.Write(".env", "SECRET=1");

        // Act
        var envelope = await harness.Find.InvokeAsync(paths: ["ok.cs", ".env"]);

        // Assert
        Assert.Equal(["ok.cs"], TextSearchHarness.Paths(envelope));
    }

    [Fact]
    public async Task FindFiles_TwoSelectors_IsSelectorInvalid()
    {
        // Arrange
        using var harness = new TextSearchHarness();

        // Act
        var envelope = await harness.Find.InvokeAsync(glob: "*.cs", regex: ".*");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.SelectorInvalid, envelope.Error.Code);
        Assert.Empty(envelope.Results);
    }

    [Fact]
    public async Task FindFiles_CursorPagination_ReturnsAllExactlyOnce()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        for (var i = 0; i < 5; i++)
        {
            harness.Write($"f{i}.cs", "x");
        }

        // Act & Assert
        var page1 = await harness.Find.InvokeAsync(glob: "*.cs", max_files: 2);
        Assert.Equal(2, page1.Results.Count);
        Assert.True(page1.Truncated);
        Assert.NotNull(page1.Cursor);

        var page2 = await harness.Find.InvokeAsync(glob: "*.cs", max_files: 2, cursor: page1.Cursor);
        var page3 = await harness.Find.InvokeAsync(glob: "*.cs", max_files: 2, cursor: page2.Cursor);

        Assert.False(page3.Truncated);
        Assert.Null(page3.Cursor);

        var all = TextSearchHarness.Paths(page1)
            .Concat(TextSearchHarness.Paths(page2))
            .Concat(TextSearchHarness.Paths(page3))
            .ToArray();
        Assert.Equal(5, all.Length);
        Assert.Equal(5, all.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task FindFiles_SkipsSymlinkedEntries_ReportsCount()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("real.cs", "1");
        if (!TryCreateSymlink(Path.Combine(harness.Root, "link.cs"), Path.Combine(harness.Root, "real.cs")))
        {
            Assert.Skip("symlink creation requires privilege not available on this runner");
        }

        // Act
        var envelope = await harness.Find.InvokeAsync(glob: "*.cs");

        // Assert
        Assert.Equal(["real.cs"], TextSearchHarness.Paths(envelope));
        Assert.Equal(1, envelope.SkippedSymlinks);
    }

    private static bool TryCreateSymlink(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}