using System.Globalization;
using RaccoonNinja.McpToolset.Server.TextSearch.Tests.TestSupport;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tests;

/// <summary>
/// Case-insensitive matching must be culture-invariant. Under <c>tr-TR</c>, a naive
/// <c>ToLower</c>/<c>IgnoreCase</c> maps <c>I</c> to a dotless <c>ı</c>, so an ASCII case-insensitive
/// match of <c>INDEX</c> against <c>index</c> would fail. These run the search and glob paths under
/// <c>tr-TR</c> to prove the ordinal/culture-invariant discipline holds.
/// </summary>
public sealed class CultureTests
{
    [Fact]
    public async Task Search_CaseInsensitive_UnderTurkishCulture_MatchesAsciiI()
    {
        // Arrange
        using var restore = new CultureScope("tr-TR");
        using var harness = new TextSearchHarness();
        harness.Write("a.txt", "INDEX\nindex\nIndex");

        // Act
        var envelope = await harness.Search.InvokeAsync(pattern: "index", glob: "*.txt");

        // Assert
        Assert.Equal(3, envelope.Results.Count);
    }

    [Fact]
    public async Task FindFiles_GlobCaseInsensitive_UnderTurkishCulture_MatchesAsciiI()
    {
        // Arrange
        using var restore = new CultureScope("tr-TR");
        using var harness = new TextSearchHarness();
        harness.Write("index.txt", "x");

        // Act
        var envelope = await harness.Find.InvokeAsync(glob: "INDEX.txt");

        // Assert
        Assert.Equal(["index.txt"], TextSearchHarness.Paths(envelope));
    }

    /// <summary>
    /// Sets <see cref="CultureInfo.CurrentCulture"/> for the current async flow and restores it on
    /// dispose. Deliberately not <c>DefaultThreadCurrentCulture</c>, which is process-global and would
    /// race the parallel test collections.
    /// </summary>
    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous;

        public CultureScope(string culture)
        {
            _previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        }

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }
}