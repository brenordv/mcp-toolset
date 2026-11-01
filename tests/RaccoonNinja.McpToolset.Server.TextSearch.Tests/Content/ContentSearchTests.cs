using System.Text;
using RaccoonNinja.McpToolset.Common.Files.Text;
using RaccoonNinja.McpToolset.Server.TextSearch.Content;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tests.Content;

public sealed class ContentSearchTests
{
    [Fact]
    public void CountMatchingLines_CountsLinesNotOccurrences()
    {
        // Arrange
        var document = Load("hit\nhit hit\nmiss");
        var matcher = LineMatcher.ForLiteral("hit", caseSensitive: false);

        // Act
        var (matchingLines, timedOut) = ContentSearch.CountMatchingLines(document, matcher, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, matchingLines);
        Assert.False(timedOut);
    }

    [Fact]
    public void CountMatchingLines_BinaryDocument_IsZero()
    {
        // Arrange
        var document = TextDocument.Load([0x89, 0x50, 0x4E, 0x47, 0x00, 0x1A, 0x0A], new EncodingDetector());
        var matcher = LineMatcher.ForLiteral("PNG", caseSensitive: true);

        // Act
        var (matchingLines, _) = ContentSearch.CountMatchingLines(document, matcher, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, matchingLines);
    }

    [Fact]
    public void CountMatchingLines_NoMatches_IsZero()
    {
        // Arrange
        var document = Load("alpha\nbeta");
        var matcher = LineMatcher.ForLiteral("gamma", caseSensitive: false);

        // Act
        var (matchingLines, timedOut) = ContentSearch.CountMatchingLines(document, matcher, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, matchingLines);
        Assert.False(timedOut);
    }

    [Fact]
    public void CountMatchingLines_CancelledToken_Throws()
    {
        // Arrange
        var document = Load("hit\nhit");
        var matcher = LineMatcher.ForLiteral("hit", caseSensitive: false);
        using var source = new CancellationTokenSource();
        source.Cancel();

        // Act
        var act = () => { _ = ContentSearch.CountMatchingLines(document, matcher, source.Token); };

        // Assert
        Assert.Throws<OperationCanceledException>(act);
    }

    #region Test Helpers

    private static TextDocument Load(string text)
        => TextDocument.Load(Encoding.UTF8.GetBytes(text), new EncodingDetector());

    #endregion
}