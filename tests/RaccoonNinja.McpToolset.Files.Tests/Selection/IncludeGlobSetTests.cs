using RaccoonNinja.McpToolset.Files.Selection;

namespace RaccoonNinja.McpToolset.Files.Tests.Selection;

public sealed class IncludeGlobSetTests
{
    [Fact]
    public void Compile_Null_ReturnsEmpty()
    {
        // Arrange
        // Act
        var set = IncludeGlobSet.Compile(null);

        // Assert
        Assert.True(set.IsEmpty);
    }

    [Fact]
    public void Compile_OnlyBlankEntries_ReturnsEmpty()
    {
        // Arrange
        // Act
        var set = IncludeGlobSet.Compile(["", "   "]);

        // Assert
        Assert.True(set.IsEmpty);
    }

    [Fact]
    public void Matches_ReturnsTrueOnlyForMatchingPaths()
    {
        // Arrange
        var set = IncludeGlobSet.Compile(["node_modules/**"]);

        // Act
        // Assert
        Assert.True(set.Matches("node_modules/lodash/index.js"));
        Assert.False(set.Matches("src/app.js"));
    }

    [Theory]
    [InlineData("node_modules", true)]
    [InlineData("node_modules/lodash", true)]
    [InlineData("node_modules/react", false)]
    [InlineData("bin", false)]
    public void CouldContain_RelatesToTheGlobPrefixBidirectionally(string directory, bool expected)
    {
        // Arrange
        var set = IncludeGlobSet.Compile(["node_modules/lodash/**"]);

        // Act
        var actual = set.CouldContain(directory);

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CouldContain_BasenameGlob_RelatesToEveryDirectory()
    {
        // Arrange
        var set = IncludeGlobSet.Compile(["*.d.ts"]);

        // Act
        // Assert
        Assert.True(set.CouldContain("node_modules"));
        Assert.True(set.CouldContain("any/deep/dir"));
    }

    [Fact]
    public void Compile_MalformedGlob_Throws()
    {
        // Arrange
        const string deeplyNested = "{{{{{a}}}}}";

        // Act
        // Assert
        Assert.Throws<RegexCompilationException>(() => IncludeGlobSet.Compile([deeplyNested]));
    }
}
