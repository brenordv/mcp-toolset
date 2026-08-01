using RaccoonNinja.McpToolset.Files.Selection;

namespace RaccoonNinja.McpToolset.Files.Tests.Selection;

public sealed class FileSelectorTests
{
    [Fact]
    public void Create_NoSelector_IsAllMode()
    {
        // Arrange
        // Act
        var selector = FileSelector.Create();

        // Assert
        Assert.Equal(SelectionMode.All, selector.Mode);
    }

    [Fact]
    public void Create_GlobOnly_IsGlobMode()
    {
        // Arrange
        // Act
        var selector = FileSelector.Create(glob: "**/*.cs");

        // Assert
        Assert.Equal(SelectionMode.Glob, selector.Mode);
        Assert.Equal("**/*.cs", selector.Glob);
    }

    [Fact]
    public void Create_RegexOnly_IsRegexMode()
    {
        // Arrange
        // Act
        var selector = FileSelector.Create(regex: ".*Test.*");

        // Assert
        Assert.Equal(SelectionMode.Regex, selector.Mode);
        Assert.Equal(".*Test.*", selector.Regex);
    }

    [Fact]
    public void Create_EmptyPathsArray_IsPathsModeSelectingNothing()
    {
        // Arrange
        // Act
        var selector = FileSelector.Create(paths: []);

        // Assert
        Assert.Equal(SelectionMode.Paths, selector.Mode);
    }

    [Theory]
    [InlineData("a", "b", null)]
    [InlineData("a", null, new[] { "p" })]
    [InlineData(null, "b", new[] { "p" })]
    [InlineData("a", "b", new[] { "p" })]
    public void Create_MoreThanOneSelector_Throws(string glob, string regex, string[] paths)
    {
        // Arrange
        // Act & Assert
        Assert.Throws<SelectorException>(() => FileSelector.Create(glob: glob, regex: regex, paths: paths));
    }

    [Fact]
    public void Create_BlankGlob_IsTreatedAsAbsent()
    {
        // Arrange
        // Act
        var selector = FileSelector.Create(glob: "   ");

        // Assert
        Assert.Equal(SelectionMode.All, selector.Mode);
    }

    [Fact]
    public void Create_BlankRoot_NormalizesToNull()
    {
        // Arrange
        // Act
        var selector = FileSelector.Create(root: "  ");

        // Assert
        Assert.Null(selector.Root);
    }

    [Fact]
    public void Create_CarriesModifiers()
    {
        // Arrange
        // Act
        var selector = FileSelector.Create(glob: "*.cs", includeIgnored: true, caseSensitive: true, maxFiles: 42);

        // Assert
        Assert.True(selector.IncludeIgnored);
        Assert.True(selector.CaseSensitive);
        Assert.Equal(42, selector.MaxFiles);
    }
}