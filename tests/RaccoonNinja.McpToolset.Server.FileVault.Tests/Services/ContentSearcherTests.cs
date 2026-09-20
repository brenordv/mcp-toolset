using System.Text;
using RaccoonNinja.McpToolset.Server.FileVault.Configuration;
using RaccoonNinja.McpToolset.Server.FileVault.Services;
using RaccoonNinja.McpToolset.Server.FileVault.Storage;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tests.Services;

/// <summary>
/// Tests for <see cref="ContentSearcher"/>: all-terms vs any-term fallback selection, substring
/// case-insensitivity, the term cap, ranking tiebreaks, and the deterministic, surrogate-safe,
/// capped snippet extraction.
/// </summary>
public sealed class ContentSearcherTests
{
    [Fact]
    public void Tokenize_CapsAtSixteenDistinctTerms()
    {
        // Arrange
        var query = string.Join(' ', Enumerable.Range(1, 20).Select(i => "t" + i));

        // Act + Assert
        Assert.Equal(16, ContentSearcher.Tokenize(query).Count);
    }

    [Fact]
    public void Search_AllTermsMatch_SelectsFullMatchesInAllTermsMode()
    {
        // Arrange
        var inputs = new[]
        {
            Input("both", "the quick brown fox"),
            Input("one", "the quick cat"),
        };

        // Act
        var result = ContentSearcher.Search(inputs, "quick fox");

        // Assert
        Assert.Equal(ListMode.AllTerms, result.Mode);
        Assert.Equal("both", Assert.Single(result.Matches).Candidate.Name);
    }

    [Fact]
    public void Search_NoFullMatch_FallsBackToAnyTerm()
    {
        // Arrange
        var inputs = new[]
        {
            Input("a", "quick"),
            Input("b", "fox"),
        };

        // Act
        var result = ContentSearcher.Search(inputs, "quick fox");

        // Assert
        Assert.Equal(ListMode.AnyTermFallback, result.Mode);
        Assert.Equal(2, result.Matches.Count);
    }

    [Fact]
    public void Search_SingleTermMiss_StaysAllTermsEmpty()
    {
        // Arrange
        var inputs = new[] { Input("a", "quick") };

        // Act
        var result = ContentSearcher.Search(inputs, "absent");

        // Assert
        Assert.Empty(result.Matches);
        Assert.Equal(ListMode.AllTerms, result.Mode);
    }

    [Fact]
    public void Search_Terms_MatchAsCaseInsensitiveSubstrings()
    {
        // Arrange
        var inputs = new[] { Input("a", "the ARGSHAPE-gotcha lives here") };

        // Act
        var result = ContentSearcher.Search(inputs, "argshape");

        // Assert
        Assert.Contains("argshape", Assert.Single(result.Matches).MatchedTerms);
    }

    [Fact]
    public void Search_Fallback_RanksMoreDistinctTermsFirst()
    {
        // Arrange
        var inputs = new[]
        {
            Input("one-term", "gamma", updatedAt: 500),
            Input("two-terms", "alpha beta", updatedAt: 100),
        };

        // Act
        var result = ContentSearcher.Search(inputs, "alpha beta gamma");

        // Assert
        Assert.Equal(ListMode.AnyTermFallback, result.Mode);
        Assert.Equal("two-terms", result.Matches[0].Candidate.Name);
        Assert.Equal("one-term", result.Matches[1].Candidate.Name);
    }

    [Fact]
    public void Search_SameTermCount_RanksNewerFirst()
    {
        // Arrange
        var inputs = new[]
        {
            Input("older", "alpha", updatedAt: 100),
            Input("newer", "alpha", updatedAt: 500),
        };

        // Act
        var result = ContentSearcher.Search(inputs, "alpha");

        // Assert
        Assert.Equal(["newer", "older"], result.Matches.Select(m => m.Candidate.Name));
    }

    [Fact]
    public void Search_Snippets_OrderedByFirstOccurrence()
    {
        // Arrange
        var inputs = new[] { Input("n", "zebra then later apple") };

        // Act
        var match = Assert.Single(ContentSearcher.Search(inputs, "apple zebra").Matches);

        // Assert
        Assert.Equal(["zebra", "apple"], match.MatchedTerms);
        Assert.Equal(["zebra", "apple"], match.Snippets.Select(s => s.Term));
    }

    [Fact]
    public void Search_Snippet_AddsEllipsisMarkersWhenCutOnBothSides()
    {
        // Arrange
        var body = new string('a', 200) + "needle" + new string('b', 200);
        var inputs = new[] { Input("n", body) };

        // Act
        var snippet = Assert.Single(Assert.Single(ContentSearcher.Search(inputs, "needle").Matches).Snippets);

        // Assert
        Assert.StartsWith("…", snippet.Text);
        Assert.EndsWith("…", snippet.Text);
        Assert.Contains("needle", snippet.Text);
    }

    [Fact]
    public void Search_Snippets_CappedAtThreePerNote()
    {
        // Arrange
        var inputs = new[] { Input("n", "aaa bbb ccc ddd eee") };

        // Act
        var match = Assert.Single(ContentSearcher.Search(inputs, "aaa bbb ccc ddd eee").Matches);

        // Assert
        Assert.Equal(5, match.MatchedTerms.Count);
        Assert.Equal(VaultConfig.MaxSnippetsPerNote, match.Snippets.Count);
    }

    [Fact]
    public void Search_Snippet_HardCapsExtractedTextAt240Chars()
    {
        // Arrange
        var longTerm = new string('x', 400);
        var inputs = new[] { Input("n", "prefix " + longTerm + " suffix") };

        // Act
        var snippet = Assert.Single(Assert.Single(ContentSearcher.Search(inputs, longTerm).Matches).Snippets);

        // Assert
        Assert.True(snippet.Text.Trim('…').Length <= VaultConfig.MaxSnippetChars);
    }

    [Fact]
    public void Search_Snippet_LeftEdgeOnSurrogate_EmitsNoUnpairedSurrogate()
    {
        // Arrange
        var body = "😀" + new string('a', 79) + "needle" + new string('b', 300);
        var inputs = new[] { Input("n", body) };

        // Act
        var snippet = Assert.Single(Assert.Single(ContentSearcher.Search(inputs, "needle").Matches).Snippets);

        // Assert
        Assert.Equal(snippet.Text, Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(snippet.Text)));
    }

    [Fact]
    public void Search_Snippet_RightEdgeOnSurrogate_EmitsNoUnpairedSurrogate()
    {
        // Arrange
        var body = "needle" + new string('a', 79) + "😀" + new string('b', 300);
        var inputs = new[] { Input("n", body) };

        // Act
        var snippet = Assert.Single(Assert.Single(ContentSearcher.Search(inputs, "needle").Matches).Snippets);

        // Assert
        Assert.Equal(snippet.Text, Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(snippet.Text)));
    }

    [Fact]
    public void Search_HugeBody_StaysWithinSnippetCaps()
    {
        // Arrange
        var body = new string('a', 100 * 1024) + " needle needle needle";
        var inputs = new[] { Input("n", body) };

        // Act
        var match = Assert.Single(ContentSearcher.Search(inputs, "needle").Matches);

        // Assert
        Assert.True(match.Snippets.Count <= VaultConfig.MaxSnippetsPerNote);
        Assert.All(match.Snippets, snippet => Assert.True(snippet.Text.Trim('…').Length <= VaultConfig.MaxSnippetChars));
    }

    private static SearchInput Input(string name, string body, long updatedAt = 100)
        => new()
        {
            Candidate = new SearchCandidateRow
            {
                Project = "p",
                Name = name,
                Summary = "s",
                CurrentVersion = 1,
                UpdatedAt = updatedAt,
                RelPath = $"p/{name}/v0001-abc.txt",
            },
            Body = body,
        };
}