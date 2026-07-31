using RaccoonNinja.McpToolset.Server.TextEdit.Content;
using RaccoonNinja.McpToolset.Server.TextEdit.Tests.TestSupport;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Tests.Content;

public sealed class ReplacerTests
{
    [Fact]
    public void Transform_Literal_ReplacesEveryOccurrenceAndCountsThem()
    {
        // Arrange
        var replacer = new Replacer("a", "b", isRegex: false, caseSensitive: false, TextEditHarness.DefaultConfig());

        // Act
        var result = replacer.Transform("a.a.a");

        // Assert
        Assert.Equal("b.b.b", result.NewText);
        Assert.Equal(3, result.MatchCount);
    }

    [Fact]
    public void Transform_LiteralCaseInsensitive_MatchesDifferentCase()
    {
        // Arrange
        var replacer = new Replacer("hello", "x", isRegex: false, caseSensitive: false, TextEditHarness.DefaultConfig());

        // Act
        var result = replacer.Transform("HELLO world");

        // Assert
        Assert.Equal("x world", result.NewText);
        Assert.Equal(1, result.MatchCount);
    }

    [Fact]
    public void Transform_LiteralCaseSensitive_DoesNotMatchDifferentCase()
    {
        // Arrange
        var replacer = new Replacer("hello", "x", isRegex: false, caseSensitive: true, TextEditHarness.DefaultConfig());

        // Act
        var result = replacer.Transform("HELLO world");

        // Assert
        Assert.Equal("HELLO world", result.NewText);
        Assert.Equal(0, result.MatchCount);
    }

    [Fact]
    public void Transform_RegexNumberedBackReference_Substitutes()
    {
        // Arrange
        var replacer = new Replacer(@"(\w+)", "[$1]", isRegex: true, caseSensitive: false, TextEditHarness.DefaultConfig());

        // Act
        var result = replacer.Transform("hi");

        // Assert
        Assert.Equal("[hi]", result.NewText);
    }

    [Fact]
    public void Transform_RegexNamedBackReference_Substitutes()
    {
        // Arrange
        var replacer = new Replacer(@"(?<word>\w+)", "<${word}>", isRegex: true, caseSensitive: false, TextEditHarness.DefaultConfig());

        // Act
        var result = replacer.Transform("hi");

        // Assert
        Assert.Equal("<hi>", result.NewText);
    }

    [Fact]
    public void Transform_RegexDoubleDollar_EmitsLiteralDollar()
    {
        // Arrange
        var replacer = new Replacer(@"\d+", "$$", isRegex: true, caseSensitive: false, TextEditHarness.DefaultConfig());

        // Act
        var result = replacer.Transform("a5b");

        // Assert
        Assert.Equal("a$b", result.NewText);
        Assert.Equal(1, result.MatchCount);
    }
}