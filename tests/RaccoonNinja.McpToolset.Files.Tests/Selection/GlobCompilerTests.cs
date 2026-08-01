using System.Globalization;
using RaccoonNinja.McpToolset.Files.Selection;

namespace RaccoonNinja.McpToolset.Files.Tests.Selection;

public sealed class GlobCompilerTests
{
    [Theory]
    [InlineData("**/*.cs", "a/b/Foo.cs", true)]
    [InlineData("**/*.cs", "Foo.cs", true)]
    [InlineData("**/*.cs", "Foo.csx", false)]
    [InlineData("**/*.cs", "Foo.cs.bak", false)]
    public void Compile_DoubleStarSlashPattern_MatchesAtAnyDepthIncludingRoot(string glob, string path, bool expected)
    {
        // Arrange
        var regex = GlobCompiler.Compile(glob);

        // Act
        var actual = regex.IsMatch(path);

        // Assert
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("*Test.cs", "src/deep/FooTest.cs", true)]
    [InlineData("*Test.cs", "FooTest.cs", true)]
    [InlineData("*Test.cs", "Foo.cs", false)]
    public void Compile_NoSlashPattern_MatchesBasenameAtAnyDepth(string glob, string path, bool expected)
    {
        // Arrange
        var regex = GlobCompiler.Compile(glob);

        // Act
        var actual = regex.IsMatch(path);

        // Assert
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("src/*.ts", "src/a.ts", true)]
    [InlineData("src/*.ts", "src/sub/a.ts", false)]
    public void Compile_SingleStar_DoesNotCrossSlash(string glob, string path, bool expected)
    {
        // Arrange
        var regex = GlobCompiler.Compile(glob);

        // Act
        var actual = regex.IsMatch(path);

        // Assert
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("src/**", "src/a/b.ts", true)]
    [InlineData("src/**", "src/a.ts", true)]
    [InlineData("**/foo", "foo", true)]
    [InlineData("**/foo", "a/b/foo", true)]
    public void Compile_DoubleStar_SpansSegments(string glob, string path, bool expected)
    {
        // Arrange
        var regex = GlobCompiler.Compile(glob);

        // Act
        var actual = regex.IsMatch(path);

        // Assert
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("file.{js,ts}", "file.js", true)]
    [InlineData("file.{js,ts}", "file.ts", true)]
    [InlineData("file.{js,ts}", "file.cs", false)]
    [InlineData("{a,{b,c}}.txt", "a.txt", true)]
    [InlineData("{a,{b,c}}.txt", "b.txt", true)]
    [InlineData("{a,{b,c}}.txt", "c.txt", true)]
    [InlineData("{a,{b,c}}.txt", "d.txt", false)]
    public void Compile_BraceGroup_BecomesAlternation(string glob, string path, bool expected)
    {
        // Arrange
        var regex = GlobCompiler.Compile(glob);

        // Act
        var actual = regex.IsMatch(path);

        // Assert
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("[ab].txt", "a.txt", true)]
    [InlineData("[ab].txt", "c.txt", false)]
    [InlineData("[a-c].txt", "b.txt", true)]
    [InlineData("[!a].txt", "b.txt", true)]
    [InlineData("[!a].txt", "a.txt", false)]
    public void Compile_CharacterClass_MatchesAndNegates(string glob, string path, bool expected)
    {
        // Arrange
        var regex = GlobCompiler.Compile(glob);

        // Act
        var actual = regex.IsMatch(path);

        // Assert
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("?.txt", "a.txt", true)]
    [InlineData("?.txt", "ab.txt", false)]
    public void Compile_QuestionMark_MatchesExactlyOneNonSlashCharacter(string glob, string path, bool expected)
    {
        // Arrange
        var regex = GlobCompiler.Compile(glob);

        // Act
        var actual = regex.IsMatch(path);

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Compile_LiteralDot_IsEscaped()
    {
        // Arrange
        var regex = GlobCompiler.Compile("a.txt");

        // Act
        var actual = regex.IsMatch("axtxt");

        // Assert
        Assert.False(actual);
    }

    [Fact]
    public void Compile_DefaultCaseInsensitive_MatchesRegardlessOfCase()
    {
        // Arrange
        var regex = GlobCompiler.Compile("README.md");

        // Act
        var actual = regex.IsMatch("readme.MD");

        // Assert
        Assert.True(actual);
    }

    [Fact]
    public void Compile_CaseSensitive_RespectsCase()
    {
        // Arrange
        var regex = GlobCompiler.Compile("README.md", caseSensitive: true);

        // Act
        var actual = regex.IsMatch("readme.md");

        // Assert
        Assert.False(actual);
    }

    [Fact]
    public void Compile_UnderTurkishCulture_MatchesAsciiCaseInsensitivelyViaInvariant()
    {
        // Arrange
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

        // Act
        bool actual;
        try
        {
            actual = GlobCompiler.Compile("index").IsMatch("INDEX");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        // Assert
        Assert.True(actual);
    }

    [Fact]
    public void Compile_NullGlob_Throws()
    {
        // Arrange
        // Act
        // Assert
        Assert.Throws<ArgumentNullException>(() => GlobCompiler.Compile(null));
    }

    [Fact]
    public void Compile_DeeplyNestedBraces_ThrowsRegexCompilationException()
    {
        // Arrange
        // Act
        // Assert
        Assert.Throws<RegexCompilationException>(() => GlobCompiler.Compile("{a,{b,{c,{d,{e,f}}}}}.txt"));
    }

    [Fact]
    public void Compile_OverlongGlob_ThrowsRegexCompilationException()
    {
        // Arrange
        // Act
        // Assert
        Assert.Throws<RegexCompilationException>(() => GlobCompiler.Compile(new string('a', 3000)));
    }
}