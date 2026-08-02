using RaccoonNinja.McpToolset.Server.TextSearch.Configuration;
using RaccoonNinja.McpToolset.Server.TextSearch.Models;
using RaccoonNinja.McpToolset.Server.TextSearch.Tests.TestSupport;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tests.Tools;

public sealed class DescribeScopeToolTests
{
    [Fact]
    public async Task DescribeScope_ReportsDenylistCapsAndEncoding()
    {
        // Arrange
        using var harness = new TextSearchHarness();

        // Act
        var envelope = await harness.Describe.InvokeAsync();

        // Assert
        Assert.Null(envelope.Error);
        var info = Assert.IsType<ScopeInfo>(Assert.Single(envelope.Results));
        Assert.Contains("**/.git/**", info.Denylist);
        Assert.Contains(".env*", info.Denylist);
        Assert.Equal("utf-8", info.DefaultEncoding);
        Assert.Equal("utf-16 code units", info.ColumnUnit);
        Assert.True(info.DenylistedOmitted);
        Assert.Equal(SearchConfig.DefaultMaxFilesCeiling, info.Caps.MaxFilesCeiling);
        Assert.Equal(SearchConfig.DefaultRegexTimeoutMs, info.Caps.RegexTimeoutMs);
    }

    [Fact]
    public async Task DescribeScope_ReportsBaseRootScopeModelAndIgnoreTiers_NoAbsolutePath()
    {
        // Arrange
        using var harness = new TextSearchHarness();

        // Act
        var envelope = await harness.Describe.InvokeAsync();

        // Assert
        var info = Assert.IsType<ScopeInfo>(Assert.Single(envelope.Results));
        Assert.Equal(Path.GetFileName(harness.Root), info.BaseRoot);
        Assert.Contains("cwd", info.ScopeModel, StringComparison.Ordinal);
        Assert.Equal([".gitignore", ".mcpignore"], info.IgnoreFiles);
        Assert.NotEmpty(info.DefaultIgnore);
        Assert.DoesNotContain(harness.Root, TextSearchHarness.ToJson(envelope), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DescribeScope_DefaultIgnoreDisabled_ReportsEmptyTier()
    {
        // Arrange
        using var harness = new TextSearchHarness(defaultIgnore: "off");

        // Act
        var envelope = await harness.Describe.InvokeAsync();

        // Assert
        var info = Assert.IsType<ScopeInfo>(Assert.Single(envelope.Results));
        Assert.Empty(info.DefaultIgnore);
    }

    [Fact]
    public async Task DescribeScope_WithPackageRoots_ListsNamesInOrder_NoAbsolutePath()
    {
        // Arrange
        using var harness = new TextSearchHarness(packageRoots: ["nuget", "cargo"]);

        // Act
        var envelope = await harness.Describe.InvokeAsync();

        // Assert
        var info = Assert.IsType<ScopeInfo>(Assert.Single(envelope.Results));
        Assert.Equal(["nuget", "cargo"], info.PackageRoots);
        Assert.Contains("@name", info.ScopeModel, StringComparison.Ordinal);
        Assert.DoesNotContain(harness.PackageDir("nuget"), TextSearchHarness.ToJson(envelope), StringComparison.Ordinal);
        Assert.DoesNotContain(harness.PackageDir("cargo"), TextSearchHarness.ToJson(envelope), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DescribeScope_NoPackageRoots_ReportsEmptyList()
    {
        // Arrange
        using var harness = new TextSearchHarness();

        // Act
        var envelope = await harness.Describe.InvokeAsync();

        // Assert
        var info = Assert.IsType<ScopeInfo>(Assert.Single(envelope.Results));
        Assert.Empty(info.PackageRoots);
    }
}