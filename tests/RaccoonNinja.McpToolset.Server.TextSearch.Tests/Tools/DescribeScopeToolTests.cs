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
        Assert.Contains("**/.git/**", info.DenylistPatterns);
        Assert.Contains(".env*", info.DenylistPatterns);
        Assert.Equal("utf-8", info.DefaultEncoding);
        Assert.Equal("utf-16 code units", info.ColumnUnit);
        Assert.True(info.DenylistedOmitted);
        Assert.Equal(SearchConfig.DefaultMaxFilesCeiling, info.Caps.MaxFilesCeiling);
        Assert.Equal(SearchConfig.DefaultRegexTimeoutMs, info.Caps.RegexTimeoutMs);
    }

    [Fact]
    public async Task DescribeScope_ListsRootsByNameAndKind_NoAbsolutePath()
    {
        // Arrange
        using var harness = new TextSearchHarness([("app", RootKind.Workspace), ("cargo", RootKind.Package)]);

        // Act
        var envelope = await harness.Describe.InvokeAsync();

        // Assert
        var info = Assert.IsType<ScopeInfo>(Assert.Single(envelope.Results));
        Assert.Equal(2, info.Roots.Count);
        Assert.Contains(info.Roots, root => root.Name == "app" && root.Kind == "workspace");
        Assert.Contains(info.Roots, root => root.Name == "cargo" && root.Kind == "package");
        Assert.DoesNotContain(harness.RootDir("app"), TextSearchHarness.ToJson(envelope), StringComparison.Ordinal);
        Assert.DoesNotContain(harness.RootDir("cargo"), TextSearchHarness.ToJson(envelope), StringComparison.Ordinal);
    }
}