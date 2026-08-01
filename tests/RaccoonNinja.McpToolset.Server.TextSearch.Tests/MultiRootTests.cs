using RaccoonNinja.McpToolset.Server.TextSearch.Configuration;
using RaccoonNinja.McpToolset.Server.TextSearch.Content;
using RaccoonNinja.McpToolset.Server.TextSearch.Errors;
using RaccoonNinja.McpToolset.Server.TextSearch.Models;
using RaccoonNinja.McpToolset.Server.TextSearch.Tests.TestSupport;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tests;

public sealed class MultiRootTests
{
    private static readonly (string, RootKind)[] AppAndCargo =
        [("app", RootKind.Workspace), ("cargo", RootKind.Package)];

    private static readonly (string, RootKind)[] TwoWorkspaces =
        [("a", RootKind.Workspace), ("b", RootKind.Workspace)];

    [Fact]
    public async Task Search_AcrossRoots_ResultsCarryDistinctRootNames()
    {
        // Arrange
        using var harness = new TextSearchHarness(TwoWorkspaces);
        harness.Write("a", "same.txt", "needle");
        harness.Write("b", "same.txt", "needle");

        // Act
        var envelope = await harness.Search.InvokeAsync(pattern: "needle", glob: "*.txt", root: "@all");

        // Assert
        var matches = envelope.Results.Cast<ContentMatch>().ToArray();
        Assert.Equal(2, matches.Length);
        Assert.Contains(matches, match => match is { Root: "a", Path: "same.txt" });
        Assert.Contains(matches, match => match is { Root: "b", Path: "same.txt" });
    }

    [Fact]
    public async Task Find_DefaultScope_SearchesWorkspaceRootsNotPackage()
    {
        // Arrange
        using var harness = new TextSearchHarness(AppAndCargo);
        harness.Write("app", "a.cs", "1");
        harness.Write("cargo", "b.cs", "2");

        // Act
        var envelope = await harness.Find.InvokeAsync(glob: "*.cs");

        // Assert
        Assert.Equal(["a.cs"], TextSearchHarness.Paths(envelope));
        Assert.Equal(["app"], TextSearchHarness.Roots(envelope));
    }

    [Fact]
    public async Task Find_PackagesTarget_SearchesPackageRoot()
    {
        // Arrange
        using var harness = new TextSearchHarness(AppAndCargo);
        harness.Write("app", "a.cs", "1");
        harness.Write("cargo", "b.cs", "2");

        // Act
        var envelope = await harness.Find.InvokeAsync(glob: "*.cs", root: "@packages");

        // Assert
        Assert.Equal(["b.cs"], TextSearchHarness.Paths(envelope));
        Assert.Equal(["cargo"], TextSearchHarness.Roots(envelope));
        Assert.True((long)harness.Metrics.Summary()["package_targeting_total"] >= 1);
    }

    [Fact]
    public async Task Find_UnknownRoot_IsInvalidArgument()
    {
        // Arrange
        using var harness = new TextSearchHarness(AppAndCargo);

        // Act
        var envelope = await harness.Find.InvokeAsync(glob: "*.cs", root: "nope");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.InvalidArgument, envelope.Error.Code);
    }

    [Fact]
    public async Task Search_PackageRootWithoutNarrowing_IsRefused()
    {
        // Arrange
        using var harness = new TextSearchHarness(AppAndCargo);
        harness.Write("cargo", "b.rs", "needle");

        // Act
        var envelope = await harness.Search.InvokeAsync(pattern: "needle", root: "@packages");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.InvalidArgument, envelope.Error.Code);
    }

    [Fact]
    public async Task Search_ExtensionsNarrowsPackageSearch_AndDenylistHolds()
    {
        // Arrange
        using var harness = new TextSearchHarness(AppAndCargo);
        harness.Write("cargo", ".env", "TOKEN=secret");
        harness.Write("cargo", "ok.rs", "let TOKEN = 1;");

        // Act
        var envelope = await harness.Search.InvokeAsync(pattern: "TOKEN", root: "@packages", extensions: ["rs", "env"]);

        // Assert
        var matches = envelope.Results.Cast<ContentMatch>().ToArray();
        Assert.NotEmpty(matches);
        Assert.All(matches, match => Assert.Equal("ok.rs", match.Path));
    }

    [Fact]
    public async Task Find_CrossRootPagination_ReturnsEachResultExactlyOnce()
    {
        // Arrange
        using var harness = new TextSearchHarness(TwoWorkspaces);
        for (var i = 0; i < 3; i++)
        {
            harness.Write("a", $"f{i}.cs", "x");
            harness.Write("b", $"f{i}.cs", "x");
        }

        var seen = new List<(string Root, string Path)>();
        string cursor = null;
        var pages = 0;

        // Act
        do
        {
            var envelope = await harness.Find.InvokeAsync(glob: "*.cs", root: "@all", max_files: 2, cursor: cursor);
            Assert.Null(envelope.Error);
            seen.AddRange(envelope.Results.Cast<FileHit>().Select(hit => (hit.Root, hit.Path)));
            cursor = envelope.Cursor;
            Assert.True(++pages < 10);
        }
        while (cursor is not null);

        // Assert
        Assert.Equal(6, seen.Count);
        Assert.Equal(6, seen.Distinct().Count());
    }

    [Fact]
    public async Task Find_CursorForDifferentTarget_IsRefused()
    {
        // Arrange
        using var harness = new TextSearchHarness(TwoWorkspaces);
        for (var i = 0; i < 3; i++)
        {
            harness.Write("a", $"f{i}.cs", "x");
            harness.Write("b", $"f{i}.cs", "x");
        }

        var page1 = await harness.Find.InvokeAsync(glob: "*.cs", root: "@all", max_files: 2);
        Assert.NotNull(page1.Cursor);

        // Act
        var envelope = await harness.Find.InvokeAsync(glob: "*.cs", root: "a", cursor: page1.Cursor);

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.InvalidArgument, envelope.Error.Code);
    }

    [Fact]
    public async Task Search_CrossRootPagination_ReturnsEachMatchExactlyOnce()
    {
        // Arrange
        using var harness = new TextSearchHarness(TwoWorkspaces);
        harness.Write("a", "f.txt", "x\nx");
        harness.Write("b", "f.txt", "x\nx");

        var seen = new List<(string Root, int Line)>();
        string cursor = null;
        var pages = 0;

        // Act
        do
        {
            var envelope = await harness.Search.InvokeAsync(pattern: "x", glob: "*.txt", root: "@all", max_results: 1, cursor: cursor);
            Assert.Null(envelope.Error);
            seen.AddRange(envelope.Results.Cast<ContentMatch>().Select(match => (match.Root, match.Line)));
            cursor = envelope.Cursor;
            Assert.True(++pages < 12);
        }
        while (cursor is not null);

        // Assert
        Assert.Equal(4, seen.Count);
        Assert.Equal(4, seen.Distinct().Count());
    }

    [Fact]
    public async Task ReadLines_MultipleRoots_RequiresRoot()
    {
        // Arrange
        using var harness = new TextSearchHarness(TwoWorkspaces);
        harness.Write("a", "x.txt", "l1\nl2");

        // Act & Assert
        var missing = await harness.ReadLines.InvokeAsync(path: "x.txt");
        Assert.NotNull(missing.Error);
        Assert.Equal(ErrorCodes.InvalidArgument, missing.Error.Code);

        var named = await harness.ReadLines.InvokeAsync(path: "x.txt", root: "a");
        Assert.Null(named.Error);
        Assert.Equal(2, named.Results.Count);
    }

    [Fact]
    public async Task ReadLines_GroupTarget_IsRefused()
    {
        // Arrange
        using var harness = new TextSearchHarness(TwoWorkspaces);
        harness.Write("a", "x.txt", "l1");

        // Act
        var envelope = await harness.ReadLines.InvokeAsync(path: "x.txt", root: "@all");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.InvalidArgument, envelope.Error.Code);
    }

    [Fact]
    public async Task Find_PageEndsExactlyOnRootBoundary_ResumesIntoNextRoot()
    {
        // Arrange
        using var harness = new TextSearchHarness(TwoWorkspaces);
        harness.Write("a", "a1.cs", "x");
        harness.Write("a", "a2.cs", "x");
        harness.Write("b", "b1.cs", "x");

        // Act & Assert
        var page1 = await harness.Find.InvokeAsync(glob: "*.cs", root: "@all", max_files: 2);
        Assert.Equal(["a", "a"], TextSearchHarness.Roots(page1));
        Assert.True(page1.Truncated);
        Assert.NotNull(page1.Cursor);

        var page2 = await harness.Find.InvokeAsync(glob: "*.cs", root: "@all", max_files: 2, cursor: page1.Cursor);
        Assert.Equal(["b"], TextSearchHarness.Roots(page2));
        Assert.Equal(["b1.cs"], TextSearchHarness.Paths(page2));
        Assert.False(page2.Truncated);
        Assert.Null(page2.Cursor);
    }

    [Fact]
    public async Task Search_AllTargetWithoutNarrowing_IsRefused()
    {
        // Arrange
        using var harness = new TextSearchHarness(AppAndCargo);

        // Act
        var envelope = await harness.Search.InvokeAsync(pattern: "x", root: "@all");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.InvalidArgument, envelope.Error.Code);
    }

    [Fact]
    public async Task Find_MalformedCursor_IsRefused()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("a.cs", "x");

        // Act
        var envelope = await harness.Find.InvokeAsync(glob: "*.cs", cursor: "not-valid-base64!!");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.InvalidArgument, envelope.Error.Code);
    }
}