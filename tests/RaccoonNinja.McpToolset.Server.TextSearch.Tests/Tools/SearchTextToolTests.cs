using RaccoonNinja.McpToolset.Server.TextSearch.Content;
using RaccoonNinja.McpToolset.Server.TextSearch.Errors;
using RaccoonNinja.McpToolset.Server.TextSearch.Models;
using RaccoonNinja.McpToolset.Server.TextSearch.Tests.TestSupport;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tests.Tools;

public sealed class SearchTextToolTests
{
    [Fact]
    public async Task Search_Literal_ReturnsLineAndColumn()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("a.txt", "first line\nhas needle here\nlast");

        // Act
        var envelope = await harness.Search.InvokeAsync(pattern: "needle", glob: "*.txt");

        // Assert
        var match = Assert.IsType<ContentMatch>(Assert.Single(envelope.Results));
        Assert.Equal("a.txt", match.Path);
        Assert.Equal(2, match.Line);
        Assert.Equal(5, match.Column);
        Assert.Equal(4, match.MatchStart);
        Assert.Equal(10, match.MatchEnd);
    }

    [Fact]
    public async Task Search_Regex_MatchesPerLine()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("a.txt", "cat\ncot\ncut\ndog");

        // Act
        var envelope = await harness.Search.InvokeAsync(pattern: "c.t", is_regex: true, glob: "*.txt");

        // Assert
        Assert.Equal(3, envelope.Results.Count);
    }

    [Fact]
    public async Task Search_ColumnCountsUtf16CodeUnits_EmojiBeforeMatch()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("emoji.txt", "x\U0001F600y match");

        // Act
        var envelope = await harness.Search.InvokeAsync(pattern: "match", glob: "*.txt");

        // Assert
        var match = Assert.IsType<ContentMatch>(Assert.Single(envelope.Results));
        Assert.Equal(6, match.Column);
    }

    [Fact]
    public async Task Search_ContextLines_IncludesSurroundingLines()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("a.txt", "one\ntwo\nHIT\nfour\nfive");

        // Act
        var envelope = await harness.Search.InvokeAsync(pattern: "HIT", glob: "*.txt", context_lines: 1);

        // Assert
        var match = Assert.IsType<ContentMatch>(Assert.Single(envelope.Results));
        Assert.Equal("two", Assert.Single(match.Before).Text);
        Assert.Equal("four", Assert.Single(match.After).Text);
    }

    [Fact]
    public async Task Search_FilesOnly_ReturnsMatchingFilesNotMatches()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("a.txt", "needle\nneedle\nneedle");
        harness.Write("b.txt", "nothing");

        // Act
        var envelope = await harness.Search.InvokeAsync(pattern: "needle", glob: "*.txt", files_only: true);

        // Assert
        var hit = Assert.IsType<FileHit>(Assert.Single(envelope.Results));
        Assert.Equal("a.txt", hit.Path);
    }

    [Fact]
    public async Task Search_MaxMatchesPerFile_CapsPerFile()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("a.txt", "x\nx\nx\nx\nx");

        // Act
        var envelope = await harness.Search.InvokeAsync(pattern: "x", glob: "*.txt", max_matches_per_file: 2);

        // Assert
        Assert.Equal(2, envelope.Results.Count);
    }

    [Fact]
    public async Task Search_OversizedQuantifier_IsPatternInvalid()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("a.txt", "aaaa");

        // Act
        var envelope = await harness.Search.InvokeAsync(pattern: "(a{1000}){1000}", is_regex: true, glob: "*.txt");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.PatternInvalid, envelope.Error.Code);
    }

    [Fact]
    public async Task Search_CatastrophicBacktracking_TripsMatchTimeoutAndRefuses()
    {
        // Arrange
        using var harness = new TextSearchHarness(regexTimeoutMs: 50);
        harness.Write("a.txt", new string('a', 40) + "!");

        // Act
        var envelope = await harness.Search.InvokeAsync(pattern: "(?=(a+)+$)", is_regex: true, glob: "*.txt");

        // Assert
        Assert.Null(envelope.Error);
        Assert.Empty(envelope.Results);
        Assert.True((long)harness.Metrics.Summary()["regex_timeouts_total"] >= 1);
    }

    [Fact]
    public async Task Search_CursorPagination_ReturnsAllMatchesExactlyOnce()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("a.txt", "x\nx");
        harness.Write("b.txt", "x\nx");
        harness.Write("c.txt", "x\nx");

        var seen = new List<(string Path, int Line)>();
        string cursor = null;
        var pages = 0;

        // Act
        do
        {
            var envelope = await harness.Search.InvokeAsync(pattern: "x", glob: "*.txt", max_results: 2, cursor: cursor);
            Assert.Null(envelope.Error);
            seen.AddRange(envelope.Results.Cast<ContentMatch>().Select(match => (match.Path, match.Line)));
            cursor = envelope.Cursor;
            Assert.True(++pages < 10);
        }
        while (cursor is not null);

        // Assert
        Assert.Equal(6, seen.Count);
        Assert.Equal(6, seen.Distinct().Count());
    }

    [Fact]
    public async Task Search_OperationBudgetExceeded_IsReported()
    {
        // Arrange
        using var harness = new TextSearchHarness(operationBudgetMs: 0);
        harness.Write("a.txt", "match");
        harness.Write("b.txt", "match");

        // Act
        var envelope = await harness.Search.InvokeAsync(pattern: "match", glob: "*.txt");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.OperationBudgetExceeded, envelope.Error.Code);
    }
}