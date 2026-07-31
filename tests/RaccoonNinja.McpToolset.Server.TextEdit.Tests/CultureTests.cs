using System.Globalization;
using RaccoonNinja.McpToolset.Server.TextEdit.Content;
using RaccoonNinja.McpToolset.Server.TextEdit.Tests.TestSupport;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Tests;

public sealed class CultureTests
{
    [Fact]
    public void Replace_CaseInsensitiveUnderTurkishCulture_StillMatchesAsciiCase()
    {
        // Arrange
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var literal = new Replacer("TITLE", "x", isRegex: false, caseSensitive: false, TextEditHarness.DefaultConfig());
            var regex = new Replacer("TITLE", "x", isRegex: true, caseSensitive: false, TextEditHarness.DefaultConfig());

            // Act
            var literalResult = literal.Transform("the title here");
            var regexResult = regex.Transform("the title here");

            // Assert
            Assert.Equal("the x here", literalResult.NewText);
            Assert.Equal("the x here", regexResult.NewText);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}