using RaccoonNinja.McpToolset.Server.TextSearch.Configuration;
using RaccoonNinja.McpToolset.Server.TextSearch.Content;
using RaccoonNinja.McpToolset.Server.TextSearch.Errors;
using RaccoonNinja.McpToolset.Server.TextSearch.Tests.TestSupport;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tests;

/// <summary>
/// The opt-in package-root model addressed through an <c>@name[/subpath]</c> <c>cwd</c>: the whole cache,
/// a scoped subpath, the three whole-cache spellings sharing one cursor identity, and every out-of-bounds
/// or unknown reference refused with a path-free, root-accurate reason. Also covers the base-<c>@dir</c>
/// versus package-name cursor non-collision and the path-free package metric.
/// </summary>
public sealed class PackageRootTests
{
    [Fact]
    public async Task Find_PackageWholeCache_ReturnsPackageRelativePaths_AndCountsPackageMetric()
    {
        // Arrange
        using var harness = new TextSearchHarness(packageRoots: ["nuget"]);
        harness.Write("base.cs", "1");
        harness.WritePackage("nuget", "Newtonsoft.Json/13.0.1/lib/a.cs", "x");
        harness.WritePackage("nuget", "Serilog/2.0/lib/b.cs", "y");

        // Act
        var envelope = await harness.Find.InvokeAsync(glob: "**/*.cs", cwd: "@nuget");

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(["Newtonsoft.Json/13.0.1/lib/a.cs", "Serilog/2.0/lib/b.cs"], TextSearchHarness.Paths(envelope));
        Assert.True((long)harness.Metrics.Summary()["package_root_calls_total"] >= 1);
        Assert.Equal(0L, (long)harness.Metrics.Summary()["whole_base_calls_total"]);
    }

    [Fact]
    public async Task Find_PackageSubpath_ScopesToOnePackage_WithSubpathRelativePaths()
    {
        // Arrange
        using var harness = new TextSearchHarness(packageRoots: ["nuget"]);
        harness.WritePackage("nuget", "Newtonsoft.Json/13.0.1/a.cs", "x");
        harness.WritePackage("nuget", "Serilog/2.0/b.cs", "y");

        // Act
        var envelope = await harness.Find.InvokeAsync(glob: "**/*.cs", cwd: "@nuget/Newtonsoft.Json");

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(["13.0.1/a.cs"], TextSearchHarness.Paths(envelope));
    }

    [Fact]
    public async Task Search_PackageSubpath_ReturnsSubpathRelativePaths()
    {
        // Arrange
        using var harness = new TextSearchHarness(packageRoots: ["cargo"]);
        harness.WritePackage("cargo", "serde-1.0/src/lib.rs", "fn needle() {}");
        harness.WritePackage("cargo", "tokio-1.0/src/lib.rs", "fn needle() {}");

        // Act
        var envelope = await harness.Search.InvokeAsync(pattern: "needle", glob: "**/*.rs", cwd: "@cargo/serde-1.0");

        // Assert
        var match = Assert.IsType<ContentMatch>(Assert.Single(envelope.Results));
        Assert.Equal("src/lib.rs", match.Path);
    }

    [Fact]
    public async Task ReadLines_FromPackageRoot_ResolvesPathRelativeToCache()
    {
        // Arrange
        using var harness = new TextSearchHarness(packageRoots: ["nuget"]);
        harness.WritePackage("nuget", "pkg/notes.txt", "l1\nl2\nl3");

        // Act
        var envelope = await harness.ReadLines.InvokeAsync(path: "pkg/notes.txt", cwd: "@nuget", start_line: 1, end_line: 2);

        // Assert
        Assert.Null(envelope.Error);
        Assert.Equal(2, envelope.Results.Count);
    }

    [Fact]
    public void Resolve_WholeCacheSpellings_MintOneCursorIdentity()
    {
        // Arrange
        using var harness = new TextSearchHarness(packageRoots: ["nuget"]);

        // Act
        var bare = harness.Resolver.Resolve("@nuget");
        var trailingSlash = harness.Resolver.Resolve("@nuget/");
        var dotSubpath = harness.Resolver.Resolve("@nuget/.");

        // Assert
        Assert.Equal("@nuget", bare.ScopeKey);
        Assert.Equal(bare.CursorScope, trailingSlash.CursorScope);
        Assert.Equal(bare.CursorScope, dotSubpath.CursorScope);
    }

    [Fact]
    public async Task Find_WholeCacheCursor_RoundTripsAcrossSpellings()
    {
        // Arrange
        using var harness = new TextSearchHarness(packageRoots: ["nuget"]);
        for (var i = 0; i < 5; i++)
        {
            harness.WritePackage("nuget", $"pkg/f{i}.cs", "x");
        }

        // Act: page one addresses @nuget, page two resumes with the @nuget/ spelling.
        var page1 = await harness.Find.InvokeAsync(glob: "**/*.cs", cwd: "@nuget", max_files: 2);
        Assert.NotNull(page1.Cursor);
        var page2 = await harness.Find.InvokeAsync(glob: "**/*.cs", cwd: "@nuget/", max_files: 2, cursor: page1.Cursor);

        // Assert
        Assert.Null(page2.Error);
        Assert.NotEmpty(page2.Results);
    }

    [Fact]
    public async Task Find_UnknownPackageRoot_IsRefusedAndCounted()
    {
        // Arrange
        using var harness = new TextSearchHarness(packageRoots: ["nuget"]);

        // Act
        var envelope = await harness.Find.InvokeAsync(glob: "*", cwd: "@cargo");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.InvalidArgument, envelope.Error.Code);
        var refusals = (IDictionary<string, object>)harness.Metrics.Summary()["refusals_total"];
        Assert.Contains("cwd_unknown_package_root", refusals.Keys);
    }

    [Fact]
    public async Task Find_PackageReference_WithNoPackageRootsConfigured_IsUnknown()
    {
        // Arrange
        using var harness = new TextSearchHarness();

        // Act
        var envelope = await harness.Find.InvokeAsync(glob: "*", cwd: "@nuget");

        // Assert
        Assert.NotNull(envelope.Error);
        var refusals = (IDictionary<string, object>)harness.Metrics.Summary()["refusals_total"];
        Assert.Contains("cwd_unknown_package_root", refusals.Keys);
    }

    [Fact]
    public async Task Find_PackageSubpathEscaping_IsRefusedWithPackageReason()
    {
        // Arrange
        using var harness = new TextSearchHarness(packageRoots: ["nuget"]);

        // Act
        var envelope = await harness.Find.InvokeAsync(glob: "*", cwd: "@nuget/../..");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.InvalidArgument, envelope.Error.Code);
        var refusals = (IDictionary<string, object>)harness.Metrics.Summary()["refusals_total"];
        Assert.Contains("cwd_outside_package_root", refusals.Keys);
    }

    [Fact]
    public async Task Find_PackageSubpathIntoDenylistedDir_IsRefused()
    {
        // Arrange
        using var harness = new TextSearchHarness(packageRoots: ["nuget"]);
        harness.WritePackage("nuget", ".git/config", "x");

        // Act
        var envelope = await harness.Find.InvokeAsync(glob: "*", cwd: "@nuget/.git");

        // Assert
        Assert.NotNull(envelope.Error);
        var refusals = (IDictionary<string, object>)harness.Metrics.Summary()["refusals_total"];
        Assert.Contains("cwd_denylisted", refusals.Keys);
    }

    [Fact]
    public void Resolve_BaseSubtreeNamedAtNuget_DoesNotAliasPackageNuget()
    {
        // Arrange
        using var harness = new TextSearchHarness(packageRoots: ["nuget"]);
        var baseAt = harness.Dir("@nuget");

        // Act
        var baseScope = harness.Resolver.Resolve(baseAt);
        var packageScope = harness.Resolver.Resolve("@nuget");

        // Assert: same readable key, different kind, so distinct cursor identities.
        Assert.Equal("@nuget", baseScope.ScopeKey);
        Assert.Equal("@nuget", packageScope.ScopeKey);
        Assert.Equal(ScopeKind.Base, baseScope.Kind);
        Assert.Equal(ScopeKind.Package, packageScope.Kind);
        Assert.NotEqual(baseScope.CursorScope, packageScope.CursorScope);
    }

    [Fact]
    public async Task Find_CursorFromBaseAtDir_IsRejectedByPackageOfSameName()
    {
        // Arrange
        using var harness = new TextSearchHarness(packageRoots: ["nuget"]);
        for (var i = 0; i < 3; i++)
        {
            harness.Write($"@nuget/f{i}.cs", "x");
        }

        var baseAt = harness.Dir("@nuget");
        var page1 = await harness.Find.InvokeAsync(glob: "*.cs", cwd: baseAt, max_files: 2);
        Assert.NotNull(page1.Cursor);

        // Act: present the base-scope cursor to the package @nuget scope.
        var envelope = await harness.Find.InvokeAsync(glob: "*.cs", cwd: "@nuget", cursor: page1.Cursor);

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.InvalidArgument, envelope.Error.Code);
    }

    [Fact]
    public async Task Find_PackageRoot_DenylistStillApplies()
    {
        // Arrange
        using var harness = new TextSearchHarness(packageRoots: ["nuget"]);
        harness.WritePackage("nuget", "pkg/ok.cs", "1");
        harness.WritePackage("nuget", "pkg/.env", "SECRET=1");

        // Act
        var envelope = await harness.Find.InvokeAsync(cwd: "@nuget");

        // Assert
        Assert.DoesNotContain("pkg/.env", TextSearchHarness.Paths(envelope));
        Assert.Contains("pkg/ok.cs", TextSearchHarness.Paths(envelope));
    }
}
