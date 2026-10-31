using System.Globalization;
using RaccoonNinja.McpToolset.Common.Files.Selection;

namespace RaccoonNinja.McpToolset.Common.Files.Tests.Selection;

public sealed class IgnoreRulesTests
{
    private const string UpperCaseGitIgnoreFileName = ".GITIGNORE";

    private static readonly string[] BracketShapeTemplates =
        ["[{0}]", "[!{0}]", "[{0}", "[a{0}z]", "a{0}b", "[{0}-x]", "[x-{0}]", "[]{0}]", "{0}"];

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
        // Arrange
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
    [InlineData("[z-ab]x", "zx", true)]
    [InlineData("[z-ab]x", "bx", true)]
    [InlineData("[z-ab]x", "ax", false)]
    [InlineData("[!z-a]x", "cx", true)]
    [InlineData("[!z-a]x", "zx", false)]
    [InlineData("[a-c-e]x", "-x", true)]
    [InlineData("[a-c-e]x", "dx", false)]
    [InlineData("[--0]x", ".x", true)]
    [InlineData("[--0]x", "1x", false)]
    [InlineData("[--0]x", "/x", false)]
    [InlineData("d[!a]x", "d/x", false)]
    [InlineData("d[.-0]x", "d/x", false)]
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
    public void Parse_LeadingCloseBracketInClass_IsLiteralMember()
    {
        // Arrange
        var rules = IgnoreRules.Parse(["[]ab]x"]);

        // Act & Assert
        Assert.True(rules.IsIgnored("]x", isDirectory: false));
        Assert.True(rules.IsIgnored("ax", isDirectory: false));
        Assert.False(rules.IsIgnored("cx", isDirectory: false));
    }

    [Fact]
    public void Parse_NegatedLeadingCloseBracketInClass_ExcludesIt()
    {
        // Arrange
        var rules = IgnoreRules.Parse(["[!]ab]x"]);

        // Act & Assert
        Assert.True(rules.IsIgnored("cx", isDirectory: false));
        Assert.False(rules.IsIgnored("]x", isDirectory: false));
        Assert.False(rules.IsIgnored("ax", isDirectory: false));
    }

    [Fact]
    public void Parse_ReversedRange_MatchesItsLowEndOnly()
    {
        // Arrange
        var rules = IgnoreRules.Parse(["[z-a]"]);

        // Act & Assert
        Assert.Equal(["[z-a]"], rules.Patterns);
        Assert.True(rules.IsIgnored("z", isDirectory: false));
        Assert.False(rules.IsIgnored("a", isDirectory: false));
        Assert.False(rules.IsIgnored("m", isDirectory: false));
    }

    [Fact]
    public void Parse_ReincludeWithNegatedClass_DoesNotCrossSlash()
    {
        // Arrange
        var rules = IgnoreRules.Parse(["*.secret", "!public[!_]*.secret"]);

        // Act & Assert
        Assert.True(rules.IsIgnored("public/x.secret", isDirectory: false));
        Assert.False(rules.IsIgnored("publicx.secret", isDirectory: false));
    }

    [Fact]
    public void Parse_AnyPrintableAsciiInBracketShapes_CompilesEveryLine()
    {
        // Arrange
        var failures = new List<(string Pattern, string Exception)>();

        // Act
        for (var c = ' '; c <= '~'; c++)
        {
            foreach (var template in BracketShapeTemplates)
            {
                var pattern = string.Format(CultureInfo.InvariantCulture, template, c);
                IgnoreRules rules = null;
                var exception = Record.Exception(() => rules = IgnoreRules.Parse([pattern]));
                if (exception is not null)
                {
                    failures.Add((pattern, exception.GetType().Name));
                }
                else if (template != "{0}" && rules.Patterns.Count != 1)
                {
                    failures.Add((pattern, "Skipped"));
                }
            }
        }

        // Assert
        Assert.Empty(failures);
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
        // Arrange
        var rules = IgnoreRules.Parse([@"with-space\   "]);

        // Act & Assert
        Assert.True(rules.IsIgnored("with-space ", isDirectory: false));
        Assert.False(rules.IsIgnored("with-space", isDirectory: false));
    }

    [Fact]
    public void IsIgnored_NestedBasePath_AnchorsRulesUnderThatDirectory()
    {
        // Arrange
        var rules = IgnoreRules.Parse(["build/", "*.tmp"], basePath: "packages/app");

        // Act & Assert
        Assert.True(rules.IsIgnored("packages/app/build", isDirectory: true));
        Assert.True(rules.IsIgnored("packages/app/nested/x.tmp", isDirectory: false));
        Assert.False(rules.IsIgnored("build", isDirectory: true));
        Assert.False(rules.IsIgnored("other/x.tmp", isDirectory: false));
    }

    [Fact]
    public void IsIgnored_NullBasePath_AnchorsRulesAtTheRoot()
    {
        // Arrange
        var rules = IgnoreRules.Parse(["logs/*.txt", "*.tmp"], basePath: null);

        // Act & Assert
        Assert.True(rules.IsIgnored("logs/a.txt", isDirectory: false));
        Assert.False(rules.IsIgnored("sub/logs/a.txt", isDirectory: false));
        Assert.True(rules.IsIgnored("nested/x.tmp", isDirectory: false));
    }

    [Fact]
    public void Combine_LaterSetOverridesEarlier()
    {
        // Arrange
        var shallow = IgnoreRules.Parse(["*.log"]);
        var deep = IgnoreRules.Parse(["!keep.log"]);

        // Act
        var combined = IgnoreRules.Combine([shallow, deep]);

        // Assert
        Assert.True(combined.IsIgnored("other.log", isDirectory: false));
        Assert.False(combined.IsIgnored("keep.log", isDirectory: false));
    }

    [Fact]
    public void Combine_MoreSpecificTierWins_OverDeeperLessSpecificSet()
    {
        // Arrange
        var dir = Directory.CreateTempSubdirectory("ignorerules-tier-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, IgnoreRules.McpIgnoreFileName), "*.hidden\n");
            var sub = Directory.CreateDirectory(Path.Combine(dir.FullName, "sub"));
            File.WriteAllText(Path.Combine(sub.FullName, IgnoreRules.GitIgnoreFileName), "!keep.hidden\n");

            // Act
            var rules = IgnoreRules.Combine([IgnoreRules.Load(dir.FullName), IgnoreRules.Load(sub.FullName, "sub")]);

            // Assert
            Assert.True(rules.IsIgnored("sub/keep.hidden", isDirectory: false));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
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

        // Assert
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

    [Fact]
    public void Load_LockedIgnoreFile_ThrowsWithRootRelativeNameOnly()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("exclusive file locking is Windows-specific");
        }

        // Arrange
        const string subdirectory = "sub";
        var dir = Directory.CreateTempSubdirectory("ignorerules-locked-");
        try
        {
            var sub = Directory.CreateDirectory(Path.Combine(dir.FullName, subdirectory));
            var ignoreFile = Path.Combine(sub.FullName, IgnoreRules.GitIgnoreFileName);
            File.WriteAllText(ignoreFile, "*.log\n");

            using (File.Open(ignoreFile, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                // Act
                var act = () => IgnoreRules.Load(sub.FullName, subdirectory);

                // Assert
                var exception = Assert.Throws<IgnoreFileReadException>(act);
                Assert.Equal("could not read ignore file 'sub/.gitignore' (IOException)", exception.Message);
                Assert.DoesNotContain(dir.FullName, exception.Message, StringComparison.Ordinal);
            }
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Load_UnlistableDirectory_ThrowsWithRootRelativeNameOnly()
    {
        SkipUnlessUnixModesApply();

        // Arrange
        const string subdirectory = "sub";
        var dir = Directory.CreateTempSubdirectory("ignorerules-unlistable-");
        var sub = Directory.CreateDirectory(Path.Combine(dir.FullName, subdirectory));
        try
        {
            File.WriteAllText(Path.Combine(sub.FullName, IgnoreRules.GitIgnoreFileName), "*.log\n");
            MakeUnlistable(sub.FullName);

            // Act
            var act = () => IgnoreRules.Load(sub.FullName, subdirectory);

            // Assert
            var exception = Assert.Throws<IgnoreDirectoryUnlistableException>(act);
            Assert.Equal("could not list directory 'sub' for ignore files (UnauthorizedAccessException)", exception.Message);
            Assert.DoesNotContain(dir.FullName, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            MakeListable(sub.FullName);
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Load_MissingDirectory_ReturnsEmpty()
    {
        // Arrange
        const string missing = "missing";
        var dir = Directory.CreateTempSubdirectory("ignorerules-missing-");
        try
        {
            // Act
            var rules = IgnoreRules.Load(Path.Combine(dir.FullName, missing), missing);

            // Assert
            Assert.Same(IgnoreRules.Empty, rules);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Load_DirectoryNamedLikeIgnoreFile_IsNotRead()
    {
        // Arrange
        const string subdirectory = "sub";
        var dir = Directory.CreateTempSubdirectory("ignorerules-dirname-");
        try
        {
            var sub = Directory.CreateDirectory(Path.Combine(dir.FullName, subdirectory));
            var namedLikeIgnoreFile = Directory.CreateDirectory(Path.Combine(sub.FullName, IgnoreRules.GitIgnoreFileName));
            File.WriteAllText(Path.Combine(namedLikeIgnoreFile.FullName, "x.txt"), "*.log\n");

            // Act
            var rules = IgnoreRules.Load(sub.FullName, subdirectory);

            // Assert
            Assert.Same(IgnoreRules.Empty, rules);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Load_IgnoreFileNameCasing_IsCaseInsensitive()
    {
        // Arrange
        var dir = Directory.CreateTempSubdirectory("ignorerules-casing-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, UpperCaseGitIgnoreFileName), "*.log\n");

            // Act
            var rules = IgnoreRules.Load(dir.FullName);

            // Assert
            Assert.True(rules.IsIgnored("a.log", isDirectory: false));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Load_CaseVariantSiblings_BothApply()
    {
        // Arrange
        var dir = Directory.CreateTempSubdirectory("ignorerules-variants-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, IgnoreRules.GitIgnoreFileName), "*.log\n");
            File.WriteAllText(Path.Combine(dir.FullName, UpperCaseGitIgnoreFileName), "*.bak\n");
            Assert.SkipUnless(Directory.EnumerateFiles(dir.FullName).Count() == 2, "The filesystem folds case.");

            // Act
            var rules = IgnoreRules.Load(dir.FullName);

            // Assert
            Assert.True(rules.IsIgnored("a.log", isDirectory: false));
            Assert.True(rules.IsIgnored("a.bak", isDirectory: false));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void IgnoreFileNames_OrdersGitignoreFirstMcpignoreLast()
    {
        // Arrange
        var names = IgnoreRules.IgnoreFileNames;

        // Assert
        Assert.Equal(IgnoreRules.GitIgnoreFileName, names[0]);
        Assert.Equal(IgnoreRules.McpIgnoreFileName, names[^1]);
    }

    [Fact]
    public void AgentIgnoreFileNames_HonorAgentFilesButNotToolScopeOrIndexOnly()
    {
        // Assert
        Assert.Contains(".cursorignore", IgnoreRules.AgentIgnoreFileNames);
        Assert.Contains(".claudeignore", IgnoreRules.AgentIgnoreFileNames);
        Assert.DoesNotContain(".cursorindexingignore", IgnoreRules.AgentIgnoreFileNames);
        Assert.DoesNotContain(".npmignore", IgnoreRules.AgentIgnoreFileNames);
        Assert.DoesNotContain(".dockerignore", IgnoreRules.AgentIgnoreFileNames);
    }

    [Fact]
    public void Load_AgentIgnoreFile_IsHonored()
    {
        // Arrange
        var dir = Directory.CreateTempSubdirectory("ignorerules-agent-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, ".cursorignore"), "secrets.txt\n");

            // Act
            var rules = IgnoreRules.Load(dir.FullName);

            // Assert
            Assert.True(rules.IsIgnored("secrets.txt", isDirectory: false));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Load_McpignoreOverridesAgentIgnoreWhichOverridesGitignore()
    {
        // Arrange
        var dir = Directory.CreateTempSubdirectory("ignorerules-order-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, IgnoreRules.GitIgnoreFileName), "*.log\n");
            File.WriteAllText(Path.Combine(dir.FullName, ".cursorignore"), "!keep.log\n*.secret\n");
            File.WriteAllText(Path.Combine(dir.FullName, IgnoreRules.McpIgnoreFileName), "!keep.secret\n");

            // Act
            var rules = IgnoreRules.Load(dir.FullName);

            // Assert
            Assert.True(rules.IsIgnored("app.log", isDirectory: false));
            Assert.False(rules.IsIgnored("keep.log", isDirectory: false));
            Assert.True(rules.IsIgnored("token.secret", isDirectory: false));
            Assert.False(rules.IsIgnored("keep.secret", isDirectory: false));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(".gitignore")]
    [InlineData("sub/.mcpignore")]
    [InlineData("a/b/.claudeignore")]
    [InlineData(".GitIgnore")]
    [InlineData("x/.GEMINIIGNORE")]
    public void IsIgnoreFileName_IgnoreFileAtAnyDepthOrCase_ReturnsTrue(string path)
    {
        // Act
        var actual = IgnoreRules.IsIgnoreFileName(path);

        // Assert
        Assert.True(actual);
    }

    [Theory]
    [InlineData("gitignore")]
    [InlineData(".gitignore.bak")]
    [InlineData("notes.txt")]
    [InlineData(".gitignore/x.txt")]
    [InlineData(".npmignore")]
    public void IsIgnoreFileName_OtherName_ReturnsFalse(string path)
    {
        // Act
        var actual = IgnoreRules.IsIgnoreFileName(path);

        // Assert
        Assert.False(actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void IsIgnoreFileName_Blank_ThrowsArgumentException(string path)
    {
        // Act
        var act = () => { IgnoreRules.IsIgnoreFileName(path); };

        // Assert
        Assert.ThrowsAny<ArgumentException>(act);
    }

    #region Test Helpers

    private static void SkipUnlessUnixModesApply()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes do not apply on Windows.");
        }

        if (Environment.IsPrivilegedProcess)
        {
            Assert.Skip("A privileged process bypasses directory mode bits.");
        }
    }

    private static void MakeUnlistable(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(directory, UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void MakeListable(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    #endregion
}