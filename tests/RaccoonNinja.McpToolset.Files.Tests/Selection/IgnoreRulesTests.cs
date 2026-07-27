using RaccoonNinja.McpToolset.Files.Selection;

namespace RaccoonNinja.McpToolset.Files.Tests.Selection;

public sealed class IgnoreRulesTests
{
    [Theory]
    [InlineData("bin", "bin", false)]
    [InlineData("bin", "src/bin", false)]
    [InlineData("bin", "src/nested/bin", false)]
    [InlineData("*.log", "app.log", false)]
    [InlineData("*.log", "logs/app.log", false)]
    [InlineData("obj", "obj", true)]
    public void IsIgnored_BasenamePattern_MatchesAtAnyDepth(string pattern, string path, bool isDirectory)
    {
        // Arrange
        var rules = IgnoreRules.Parse([pattern]);

        // Act
        var actual = rules.IsIgnored(path, isDirectory);

        // Assert
        Assert.True(actual);
    }

    [Theory]
    [InlineData("/build", "build")]
    [InlineData("src/generated", "src/generated")]
    public void IsIgnored_AnchoredPattern_MatchesOnlyAtItsLocation(string pattern, string matching)
    {
        // Arrange
        var rules = IgnoreRules.Parse([pattern]);

        // Act & Assert
        Assert.True(rules.IsIgnored(matching, isDirectory: false));
        Assert.False(rules.IsIgnored("nested/" + matching, isDirectory: false));
    }

    [Fact]
    public void IsIgnored_BlankAndCommentLines_ContributeNoRules()
    {
        // Arrange
        var rules = IgnoreRules.Parse(["", "   ", "# a comment", "\t"]);

        // Act & Assert
        Assert.Empty(rules.Patterns);
        Assert.False(rules.IsIgnored("anything.txt", isDirectory: false));
    }

    [Fact]
    public void IsIgnored_DirectoryOnlyPattern_MatchesDirectoriesNotFiles()
    {
        // Arrange
        var rules = IgnoreRules.Parse(["cache/"]);

        // Act & Assert
        Assert.True(rules.IsIgnored("cache", isDirectory: true));
        Assert.False(rules.IsIgnored("cache", isDirectory: false));
    }

    [Fact]
    public void IsIgnored_Negation_ReincludesWithLastMatchWins()
    {
        // Arrange
        var rules = IgnoreRules.Parse(["*.log", "!keep.log"]);

        // Act & Assert
        Assert.True(rules.IsIgnored("debug.log", isDirectory: false));
        Assert.False(rules.IsIgnored("keep.log", isDirectory: false));
    }

    [Fact]
    public void IsIgnored_ReexcludeAfterNegation_LastMatchWins()
    {
        // Arrange: a later exclude overrides an earlier re-include.
        var rules = IgnoreRules.Parse(["*.log", "!keep.log", "keep.log"]);

        // Act
        var actual = rules.IsIgnored("keep.log", isDirectory: false);

        // Assert
        Assert.True(actual);
    }

    [Fact]
    public void IsIgnored_SingleStar_DoesNotCrossSlash()
    {
        // Arrange
        var rules = IgnoreRules.Parse(["logs/*.txt"]);

        // Act & Assert
        Assert.True(rules.IsIgnored("logs/a.txt", isDirectory: false));
        Assert.False(rules.IsIgnored("logs/nested/a.txt", isDirectory: false));
    }

    [Fact]
    public void IsIgnored_LeadingDoubleStar_MatchesAnyDepthIncludingZero()
    {
        // Arrange
        var rules = IgnoreRules.Parse(["**/target"]);

        // Act & Assert
        Assert.True(rules.IsIgnored("target", isDirectory: true));
        Assert.True(rules.IsIgnored("a/b/target", isDirectory: true));
    }

    [Fact]
    public void IsIgnored_TrailingDoubleStar_MatchesContentsNotTheDirectory()
    {
        // Arrange
        var rules = IgnoreRules.Parse(["dist/**"]);

        // Act & Assert
        Assert.False(rules.IsIgnored("dist", isDirectory: true));
        Assert.True(rules.IsIgnored("dist/app.js", isDirectory: false));
        Assert.True(rules.IsIgnored("dist/assets/logo.png", isDirectory: false));
    }

    [Theory]
    [InlineData("[abc].txt", "b.txt", true)]
    [InlineData("[abc].txt", "d.txt", false)]
    [InlineData("[!abc].txt", "d.txt", true)]
    [InlineData("[!abc].txt", "a.txt", false)]
    [InlineData("file?.txt", "file1.txt", true)]
    [InlineData("file?.txt", "file12.txt", false)]
    public void IsIgnored_CharacterClassAndQuestionMark_MatchAsGlob(string pattern, string path, bool expected)
    {
        // Arrange
        var rules = IgnoreRules.Parse([pattern]);

        // Act
        var actual = rules.IsIgnored(path, isDirectory: false);

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IsIgnored_EscapedHash_IsTreatedAsLiteralNotComment()
    {
        // Arrange
        var rules = IgnoreRules.Parse([@"\#notes.txt"]);

        // Act & Assert
        Assert.Single(rules.Patterns);
        Assert.True(rules.IsIgnored("#notes.txt", isDirectory: false));
    }

    [Fact]
    public void IsIgnored_EscapedTrailingSpace_IsPreservedInPattern()
    {
        // Arrange: "with-space\ " keeps one trailing space; the unescaped run is stripped.
        var rules = IgnoreRules.Parse([@"with-space\   "]);

        // Act & Assert
        Assert.True(rules.IsIgnored("with-space ", isDirectory: false));
        Assert.False(rules.IsIgnored("with-space", isDirectory: false));
    }

    [Fact]
    public void IsIgnored_NestedBasePath_AnchorsRulesUnderThatDirectory()
    {
        // Arrange: an ignore file living in "packages/app".
        var rules = IgnoreRules.Parse(["build/", "*.tmp"], basePath: "packages/app");

        // Act & Assert
        Assert.True(rules.IsIgnored("packages/app/build", isDirectory: true));
        Assert.True(rules.IsIgnored("packages/app/nested/x.tmp", isDirectory: false));
        Assert.False(rules.IsIgnored("build", isDirectory: true));
        Assert.False(rules.IsIgnored("other/x.tmp", isDirectory: false));
    }

    [Fact]
    public void Combine_LaterSetOverridesEarlier()
    {
        // Arrange: a deep ignore file re-includes what a shallow one excludes.
        var shallow = IgnoreRules.Parse(["*.log"]);
        var deep = IgnoreRules.Parse(["!keep.log"]);

        // Act
        var combined = IgnoreRules.Combine([shallow, deep]);

        // Assert
        Assert.True(combined.IsIgnored("other.log", isDirectory: false));
        Assert.False(combined.IsIgnored("keep.log", isDirectory: false));
    }

    [Fact]
    public void Patterns_ExposesSourceLinesInOrder()
    {
        // Arrange
        var rules = IgnoreRules.Parse(["bin", "# comment", "*.log", "!keep.log"]);

        // Act
        var patterns = rules.Patterns;

        // Assert
        Assert.Equal(["bin", "*.log", "!keep.log"], patterns);
    }

    [Fact]
    public void IsIgnored_CasingFollowsPlatform()
    {
        // Arrange
        var rules = IgnoreRules.Parse(["build/"]);

        // Act
        var actual = rules.IsIgnored("BUILD", isDirectory: true);

        // Assert: case-sensitive on Linux, case-insensitive elsewhere (matches RootConfinement).
        Assert.Equal(!OperatingSystem.IsLinux(), actual);
    }

    [Fact]
    public void Load_ReadsGitignoreAndMcpignore_WithMcpignoreOverriding()
    {
        // Arrange
        var dir = Directory.CreateTempSubdirectory("ignorerules-load-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, IgnoreRules.GitIgnoreFileName), "*.log\nbin/\n");
            File.WriteAllText(Path.Combine(dir.FullName, IgnoreRules.McpIgnoreFileName), "!keep.log\n");

            // Act
            var rules = IgnoreRules.Load(dir.FullName);

            // Assert
            Assert.True(rules.IsIgnored("app.log", isDirectory: false));
            Assert.True(rules.IsIgnored("bin", isDirectory: true));
            Assert.False(rules.IsIgnored("keep.log", isDirectory: false));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Load_NoIgnoreFiles_ReturnsEmpty()
    {
        // Arrange
        var dir = Directory.CreateTempSubdirectory("ignorerules-empty-");
        try
        {
            // Act
            var rules = IgnoreRules.Load(dir.FullName);

            // Assert
            Assert.Same(IgnoreRules.Empty, rules);
            Assert.False(rules.IsIgnored("anything", isDirectory: false));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}