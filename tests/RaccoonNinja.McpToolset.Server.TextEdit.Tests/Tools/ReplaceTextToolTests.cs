using RaccoonNinja.McpToolset.Server.TextEdit.Errors;
using RaccoonNinja.McpToolset.Server.TextEdit.Models;
using RaccoonNinja.McpToolset.Server.TextEdit.Tests.TestSupport;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Tests.Tools;

public sealed class ReplaceTextToolTests
{
    private const string BacktrackingPattern = "(?=(a+)+$)";
    private const string BacktrackingReplacement = "x";
    private const string TextFileGlob = "*.txt";
    private const int RegexTimeoutMs = 50;
    private const string RegexTimeoutReason = "regex_timeout";
    private const string RegexTimeoutsTotal = "regex_timeouts_total";
    private static readonly string BacktrackingInput = new string('a', 40) + "!";

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

    [Fact]
    public async Task InvokeAsync_MoreFilesThanMaxFiles_ReportsTruncated()
    {
        // Arrange
        const string original = "hello world";
        const string pastCapFile = "c.txt";
        const int maxFiles = 2;
        using var harness = new TextEditHarness();
        harness.WriteText("a.txt", original);
        harness.WriteText("b.txt", original);
        harness.WriteText(pastCapFile, original);

        // Act
        var envelope = await harness.Replace.InvokeAsync("world", "text", glob: "*.txt", max_files: maxFiles);

        // Assert
        Assert.Null(envelope.Error);
        Assert.True(envelope.Truncated);
        var result = Assert.IsType<MutationResult>(Assert.Single(envelope.Results));
        Assert.Equal(maxFiles, result.Attempted);
        Assert.Equal(original, harness.ReadText(pastCapFile));
        Assert.Equal(1L, (long)harness.Metrics.Summary()["truncations_total"]);
    }

    [Fact]
    public async Task InvokeAsync_SelectionUnderMaxFiles_ReportsSkippedSymlinksAndNotTruncated()
    {
        // Arrange
        using var harness = new TextEditHarness();
        harness.WriteText("a.txt", "hello world");

        // Act
        var envelope = await harness.Replace.InvokeAsync("world", "text", glob: "*.txt", max_files: 2);

        // Assert
        Assert.Null(envelope.Error);
        Assert.False(envelope.Truncated);
        Assert.Equal(0, envelope.SkippedSymlinks);
        Assert.Equal(0L, (long)harness.Metrics.Summary()["truncations_total"]);
    }

    [Fact]
    public async Task InvokeAsync_RegexTimeoutOnOneFile_CountsOneRegexTimeout()
    {
        // Arrange
        const string slowFile = "slow.txt";
        var config = TextEditHarness.DefaultConfig() with { RegexTimeout = TimeSpan.FromMilliseconds(RegexTimeoutMs) };
        using var harness = new TextEditHarness(config);
        harness.WriteText(slowFile, BacktrackingInput);
        harness.WriteText("fast.txt", "hello");

        // Act
        var envelope = await harness.Replace.InvokeAsync(
            BacktrackingPattern,
            BacktrackingReplacement,
            glob: TextFileGlob,
            is_regex: true,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var result = Assert.IsType<MutationResult>(Assert.Single(envelope.Results));
        var refused = Assert.Single(result.Files, file => file.Path == slowFile);
        Assert.Equal(RegexTimeoutReason, refused.RefusalReason);
        Assert.Equal(BacktrackingInput, harness.ReadText(slowFile));
        Assert.Equal(1L, (long)harness.Metrics.Summary()[RegexTimeoutsTotal]);
    }

    [Fact]
    public async Task InvokeAsync_RegexTimeoutOnTwoFiles_CountsOnePerFile()
    {
        // Arrange
        var config = TextEditHarness.DefaultConfig() with { RegexTimeout = TimeSpan.FromMilliseconds(RegexTimeoutMs) };
        using var harness = new TextEditHarness(config);
        harness.WriteText("slow-a.txt", BacktrackingInput);
        harness.WriteText("slow-b.txt", BacktrackingInput);

        // Act
        var envelope = await harness.Replace.InvokeAsync(
            BacktrackingPattern,
            BacktrackingReplacement,
            glob: TextFileGlob,
            is_regex: true,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(envelope.Error);
        var result = Assert.IsType<MutationResult>(Assert.Single(envelope.Results));
        Assert.Equal(2, result.Refused);
        Assert.All(result.Files, file => Assert.Equal(RegexTimeoutReason, file.RefusalReason));
        Assert.Equal(2L, (long)harness.Metrics.Summary()[RegexTimeoutsTotal]);
    }

    [Fact]
    public async Task InvokeAsync_RegexTimeoutWithExpectedMatchCount_CountsTimeoutAndReportsMismatch()
    {
        // Arrange
        const string slowFile = "slow.txt";
        const int expectedMatchCount = 1;
        var config = TextEditHarness.DefaultConfig() with { RegexTimeout = TimeSpan.FromMilliseconds(RegexTimeoutMs) };
        using var harness = new TextEditHarness(config);
        harness.WriteText(slowFile, BacktrackingInput);

        // Act
        var envelope = await harness.Replace.InvokeAsync(
            BacktrackingPattern,
            BacktrackingReplacement,
            glob: TextFileGlob,
            is_regex: true,
            expected_match_count: expectedMatchCount,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ErrorCodes.ExpectedMatchCountMismatch, envelope.Error.Code);
        Assert.Equal(BacktrackingInput, harness.ReadText(slowFile));
        Assert.Equal(1L, (long)harness.Metrics.Summary()[RegexTimeoutsTotal]);
    }
}