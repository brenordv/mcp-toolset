using RaccoonNinja.McpToolset.Server.TextSearch.Content;
using RaccoonNinja.McpToolset.Server.TextSearch.Errors;
using RaccoonNinja.McpToolset.Server.TextSearch.Models;
using RaccoonNinja.McpToolset.Server.TextSearch.Tests.TestSupport;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tests;

/// <summary>
/// The single-base-root plus per-call <c>cwd</c> model: an omitted <c>cwd</c> searches the whole base
/// with base-relative paths, a <c>cwd</c> scopes to one subtree with cwd-relative paths, and every
/// out-of-bounds <c>cwd</c> is refused. Also covers glob-scoped <c>include_ignored</c>, the extended
/// denylist, and cursor scope identity.
/// </summary>
public sealed class CwdScopeTests
{
    [Fact]
    public async Task Find_OmittedCwd_SearchesWholeBase_WithBaseRelativePaths()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("proj-a/x.cs", "1");
        harness.Write("proj-b/y.cs", "2");

        // Act
        var envelope = await harness.Find.InvokeAsync(glob: "**/*.cs");

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(["proj-a/x.cs", "proj-b/y.cs"], TextSearchHarness.Paths(envelope));
        Assert.True((long)harness.Metrics.Summary()["whole_base_calls_total"] >= 1);
    }

    [Fact]
    public async Task Find_ScopedCwd_ReturnsCwdRelativePaths_AndNoWholeBaseMetric()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("proj/src/a.cs", "1");
        harness.Write("other/b.cs", "2");
        var cwd = harness.Dir("proj");

        // Act
        var envelope = await harness.Find.InvokeAsync(glob: "**/*.cs", cwd: cwd);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(["src/a.cs"], TextSearchHarness.Paths(envelope));
        Assert.Equal(0L, (long)harness.Metrics.Summary()["whole_base_calls_total"]);
    }

    [Fact]
    public async Task Search_ScopedCwd_ReturnsCwdRelativePaths()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("proj/a.txt", "needle here");
        harness.Write("outside/b.txt", "needle here");
        var cwd = harness.Dir("proj");

        // Act
        var envelope = await harness.Search.InvokeAsync(pattern: "needle", glob: "*.txt", cwd: cwd);

        // Assert
        var match = Assert.IsType<ContentMatch>(Assert.Single(envelope.Results));
        Assert.Equal("a.txt", match.Path);
    }

    [Fact]
    public async Task ReadLines_ScopedCwd_ResolvesPathRelativeToCwd()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("proj/notes.txt", "l1\nl2\nl3");
        var cwd = harness.Dir("proj");

        // Act
        var envelope = await harness.ReadLines.InvokeAsync(path: "notes.txt", cwd: cwd, start_line: 2, end_line: 3);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(2, envelope.Results.Count);
    }

    [Fact]
    public async Task Find_CwdEscapingBase_IsRefusedAndCounted()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("a.cs", "1");
        var escaping = Path.Combine(harness.Root, "..");

        // Act
        var envelope = await harness.Find.InvokeAsync(glob: "*.cs", cwd: escaping);

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.InvalidArgument, envelope.Error.Code);
        var refusals = (IDictionary<string, object>)harness.Metrics.Summary()["refusals_total"];
        Assert.Contains("cwd_outside_base", refusals.Keys);
    }

    [Fact]
    public async Task Find_CwdPointingAtFile_IsRefused()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("note.txt", "x");
        var fileCwd = Path.Combine(harness.Root, "note.txt");

        // Act
        var envelope = await harness.Find.InvokeAsync(glob: "*.cs", cwd: fileCwd);

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.InvalidArgument, envelope.Error.Code);
    }

    [Fact]
    public async Task Find_CwdInsideDenylistedDir_IsRefused()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        var gitCwd = harness.Dir(".git");

        // Act
        var envelope = await harness.Find.InvokeAsync(glob: "*", cwd: gitCwd);

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.InvalidArgument, envelope.Error.Code);
        var refusals = (IDictionary<string, object>)harness.Metrics.Summary()["refusals_total"];
        Assert.Contains("cwd_denylisted", refusals.Keys);
    }

    [Fact]
    public async Task Find_ExtraDeny_OmitsExtendedDenylistedFileEndToEnd()
    {
        // Arrange
        using var harness = new TextSearchHarness(extraDeny: "*.secret");
        harness.Write("ok.cs", "1");
        harness.Write("token.secret", "SECRET");

        // Act
        var envelope = await harness.Find.InvokeAsync();

        // Assert
        Assert.Equal(["ok.cs"], TextSearchHarness.Paths(envelope));
    }

    [Fact]
    public async Task Find_IncludeIgnoredGlob_DoesNotReIncludeGitignored_ButIsStillCounted()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write(".gitignore", "*.log\n");
        harness.Write("skip.log", "x");

        // Act
        var envelope = await harness.Find.InvokeAsync(glob: "*.log", include_ignored: ["*.log"]);

        // Assert: include_ignored can no longer re-include a .gitignore'd file, but the call is still counted.
        Assert.Empty(TextSearchHarness.Paths(envelope));
        Assert.True((long)harness.Metrics.Summary()["include_ignored_calls_total"] >= 1);
    }

    [Fact]
    public async Task Find_IncludeIgnoredGlob_StillReIncludesDefaultTier()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("node_modules/pkg/index.js", "x");

        // Act
        var envelope = await harness.Find.InvokeAsync(glob: "**/*.js", include_ignored: ["node_modules/**"]);

        // Assert: node_modules is default-tier ignored (not a .gitignore rule), so it stays re-includable.
        Assert.Equal(["node_modules/pkg/index.js"], TextSearchHarness.Paths(envelope));
    }

    [Fact]
    public async Task Find_AncestorGitignoreDirectory_HidesSubtreeInScopedCwd()
    {
        // Arrange: a base-root .gitignore ignores the whole secrets/ directory. A call scoped into it must
        // return nothing: the ignore file sits above the cwd (a directory rule), and the boundary is anchored
        // at the base root, not the cwd.
        using var harness = new TextSearchHarness();
        harness.Write(".gitignore", "secrets/\n");
        harness.Write("secrets/api-key.txt", "SECRET");

        // Act
        var envelope = await harness.Find.InvokeAsync(glob: "*", cwd: harness.Dir("secrets"));

        // Assert
        Assert.Empty(TextSearchHarness.Paths(envelope));
    }

    [Fact]
    public async Task Find_IncludeIgnoredGlob_NeverReachesDenylistedFile()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write(".env", "SECRET=1");
        harness.Write("keep.cs", "1");

        // Act
        var envelope = await harness.Find.InvokeAsync(include_ignored: ["**/*"]);

        // Assert
        Assert.DoesNotContain(".env", TextSearchHarness.Paths(envelope));
    }

    [Fact]
    public async Task Find_CursorRoundTrips_WithinScope()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        for (var i = 0; i < 5; i++)
        {
            harness.Write($"proj/f{i}.cs", "x");
        }

        var cwd = harness.Dir("proj");
        var seen = new List<string>();
        string cursor = null;
        var pages = 0;

        // Act
        do
        {
            var envelope = await harness.Find.InvokeAsync(glob: "*.cs", cwd: cwd, max_files: 2, cursor: cursor);
            Assert.Null(envelope.Error);
            seen.AddRange(TextSearchHarness.Paths(envelope));
            cursor = envelope.Cursor;
            Assert.True(++pages < 10);
        }
        while (cursor is not null);

        // Assert
        Assert.Equal(5, seen.Count);
        Assert.Equal(5, seen.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Find_CursorFromDifferentScope_IsRejected()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("a.cs", "x");
        harness.Write("b.cs", "x");
        harness.Write("c.cs", "x");
        harness.Write("proj/d.cs", "x");
        var page1 = await harness.Find.InvokeAsync(glob: "*.cs", max_files: 2);
        Assert.NotNull(page1.Cursor);
        var cwd = harness.Dir("proj");

        // Act
        var envelope = await harness.Find.InvokeAsync(glob: "*.cs", cwd: cwd, cursor: page1.Cursor);

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.InvalidArgument, envelope.Error.Code);
    }

    [Fact]
    public async Task Search_FilesOnly_ReturnsScopeRelativePaths()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("proj/hit.txt", "needle\nneedle");
        harness.Write("proj/miss.txt", "nothing");
        var cwd = harness.Dir("proj");

        // Act
        var envelope = await harness.Search.InvokeAsync(pattern: "needle", glob: "*.txt", cwd: cwd, files_only: true);

        // Assert
        var hit = Assert.IsType<FileHit>(Assert.Single(envelope.Results));
        Assert.Equal("hit.txt", hit.Path);
    }
}