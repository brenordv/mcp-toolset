using System.Globalization;
using RaccoonNinja.McpToolset.Files.Selection;

namespace RaccoonNinja.McpToolset.Files.Tests.Selection;

public sealed class SafeRegexCompilerTests
{
    private static readonly SafeRegexOptions Defaults = new();

    [Fact]
    public void Compile_PlainPattern_UsesNonBacktracking()
    {
        // Arrange
        // Act
        var compiled = SafeRegexCompiler.Compile("foo.*bar", Defaults);

        // Assert
        Assert.True(compiled.IsNonBacktracking);
        Assert.Matches(compiled.Regex, "foo123bar");
    }

    [Fact]
    public void Compile_CaseInsensitiveByDefault()
    {
        // Arrange
        // Act
        var compiled = SafeRegexCompiler.Compile("hello", Defaults);

        // Assert
        Assert.Matches(compiled.Regex, "HELLO");
    }

    [Fact]
    public void Compile_CaseSensitive_RespectsCase()
    {
        // Arrange
        var options = new SafeRegexOptions { CaseSensitive = true };

        // Act
        var compiled = SafeRegexCompiler.Compile("hello", options);

        // Assert
        Assert.DoesNotMatch(compiled.Regex, "HELLO");
        Assert.Matches(compiled.Regex, "hello");
    }

    [Fact]
    public void Compile_CultureInvariant_MatchesIgnoreCaseUnderTurkishCulture()
    {
        // Arrange
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

            // Act
            var compiled = SafeRegexCompiler.Compile("index", Defaults);

            // Assert
            Assert.Matches(compiled.Regex, "INDEX");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("foo(?=bar)")]
    [InlineData(@"(\w+)\s+\1")]
    [InlineData("foo(?<!bar)")]
    public void Compile_ConstructUnsupportedByNonBacktracking_FallsBackToBacktracking(string pattern)
    {
        // Arrange
        // Act
        var compiled = SafeRegexCompiler.Compile(pattern, Defaults);

        // Assert
        Assert.False(compiled.IsNonBacktracking);
    }

    [Fact]
    public void Compile_BackreferencePattern_StillMatchesAfterFallback()
    {
        // Arrange
        var compiled = SafeRegexCompiler.Compile(@"(\w+)\s+\1", Defaults);

        // Act & Assert
        Assert.Matches(compiled.Regex, "the the");
        Assert.DoesNotMatch(compiled.Regex, "the cat");
    }

    [Theory]
    [InlineData("(")]
    [InlineData("[a-")]
    [InlineData("*abc")]
    public void Compile_InvalidPattern_ThrowsRegexCompilationException(string pattern)
    {
        // Arrange
        // Act & Assert
        Assert.Throws<RegexCompilationException>(() => SafeRegexCompiler.Compile(pattern, Defaults));
    }

    [Fact]
    public void Compile_PatternTooLong_Throws()
    {
        // Arrange
        var pattern = new string('a', 3000);
        var options = new SafeRegexOptions { MaxPatternLength = 2048 };

        // Act & Assert
        Assert.Throws<RegexCompilationException>(() => SafeRegexCompiler.Compile(pattern, options));
    }

    [Fact]
    public void Compile_OversizedSingleBoundedQuantifier_Throws()
    {
        // Arrange
        var options = new SafeRegexOptions { MaxRepetitionProduct = 1000 };

        // Act & Assert
        Assert.Throws<RegexCompilationException>(() => SafeRegexCompiler.Compile("a{2000}", options));
    }

    [Fact]
    public void Compile_NestedBoundedQuantifierProduct_Throws()
    {
        // Arrange
        // Act & Assert
        Assert.Throws<RegexCompilationException>(() => SafeRegexCompiler.Compile("(a{1000}){1000}", Defaults));
    }

    [Fact]
    public void Compile_ModestBoundedQuantifier_IsAllowed()
    {
        // Arrange
        // Act
        var compiled = SafeRegexCompiler.Compile("a{5}b{5}", Defaults);

        // Assert
        Assert.Matches(compiled.Regex, "aaaaabbbbb");
    }

    [Fact]
    public void Compile_BracesInsideCharacterClass_AreNotCountedAsQuantifiers()
    {
        // Arrange
        var options = new SafeRegexOptions { MaxRepetitionProduct = 1000 };

        // Act
        var compiled = SafeRegexCompiler.Compile("[a{2000}]+", options);

        // Assert
        Assert.Matches(compiled.Regex, "a{2}0");
    }

    [Fact]
    public void Compile_EscapedBrace_IsNotCountedAsQuantifier()
    {
        // Arrange
        var options = new SafeRegexOptions { MaxRepetitionProduct = 1000 };

        // Act
        var compiled = SafeRegexCompiler.Compile(@"a\{2000\}", options);

        // Assert
        Assert.Matches(compiled.Regex, "a{2000}");
    }

    [Fact]
    public void Compile_AppliesMatchTimeoutToTheRegex()
    {
        // Arrange
        var options = new SafeRegexOptions { MatchTimeout = TimeSpan.FromMilliseconds(250) };

        // Act
        var compiled = SafeRegexCompiler.Compile("abc", options);

        // Assert
        Assert.Equal(TimeSpan.FromMilliseconds(250), compiled.Regex.MatchTimeout);
    }

    [Fact]
    public void Compile_NullPattern_Throws()
    {
        // Arrange
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => SafeRegexCompiler.Compile(null, Defaults));
    }
}