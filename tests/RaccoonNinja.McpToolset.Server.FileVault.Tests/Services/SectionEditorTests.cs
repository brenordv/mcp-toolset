using RaccoonNinja.McpToolset.Server.FileVault.Errors;
using RaccoonNinja.McpToolset.Server.FileVault.Services;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tests.Services;

/// <summary>Unit tests for <see cref="SectionEditor"/> markdown section splicing (Rust parity contract).</summary>
public class SectionEditorTests
{
    [Fact]
    public void SpliceSection_HeadingWithFollowingSibling_ReplacesBodyAndPreservesRestVerbatim()
    {
        // Arrange
        const string source = "# Title\n\nintro text\n\n## First\nold line one\nold line two\n\n## Second\nkeep this\n";

        // Act
        var result = SectionEditor.SpliceSection(source, "First", "new body");

        // Assert
        Assert.Equal("# Title\n\nintro text\n\n## First\nnew body\n\n## Second\nkeep this\n", result);
    }

    [Fact]
    public void SpliceSection_LastSectionInDocument_ReplacesToEndWithoutExtraBlankLine()
    {
        // Arrange
        const string source = "# Only\nold\n";

        // Act
        var result = SectionEditor.SpliceSection(source, "Only", "fresh");

        // Assert
        Assert.Equal("# Only\nfresh\n", result);
    }

    [Fact]
    public void SpliceSection_SectionAtDocumentStart_ReplacesOnlyItsBody()
    {
        // Arrange
        const string source = "# First\nold\n\n# Second\ntail\n";

        // Act
        var result = SectionEditor.SpliceSection(source, "First", "new");

        // Assert
        Assert.Equal("# First\nnew\n\n# Second\ntail\n", result);
    }

    [Fact]
    public void SpliceSection_NestedSubsections_ConsumedWithParentSection()
    {
        // Arrange
        const string source = "## Parent\nold\n\n### Child\nchild body\n\n## Sibling\ntail\n";

        // Act
        var result = SectionEditor.SpliceSection(source, "Parent", "replaced");

        // Assert
        Assert.Equal("## Parent\nreplaced\n\n## Sibling\ntail\n", result);
    }

    [Fact]
    public void SpliceSection_NewBodyWithTrailingNewlines_CollapsesToSingleTrailingNewline()
    {
        // Arrange
        const string source = "# A\nold\n";

        // Act
        var result = SectionEditor.SpliceSection(source, "A", "line one\nline two\n\n\n");

        // Assert
        Assert.Equal("# A\nline one\nline two\n", result);
    }

    [Fact]
    public void SpliceSection_EmptyNewBody_LeavesSingleNewlineUnderHeading()
    {
        // Arrange
        const string source = "# A\nold stuff\n";

        // Act
        var result = SectionEditor.SpliceSection(source, "A", string.Empty);

        // Assert
        Assert.Equal("# A\n\n", result);
    }

    [Theory]
    [InlineData("Section Two")]
    [InlineData("## Section Two")]
    [InlineData("####   Section Two")]
    [InlineData("  Section Two  ")]
    public void SpliceSection_TargetWithLeadingHashesOrPadding_MatchesHeading(string target)
    {
        // Arrange
        const string source = "# Top\n\n## Section Two\nold\n";

        // Act
        var result = SectionEditor.SpliceSection(source, target, "new");

        // Assert
        Assert.Equal("# Top\n\n## Section Two\nnew\n", result);
    }

    [Fact]
    public void SpliceSection_HeadingLineWithTrailingWhitespace_MatchesAndPreservesHeadingLineVerbatim()
    {
        // Arrange
        const string source = "## Padded  \nold\n\n## Next\ntail\n";

        // Act
        var result = SectionEditor.SpliceSection(source, "Padded", "new");

        // Assert
        Assert.Equal("## Padded  \nnew\n\n## Next\ntail\n", result);
    }

    [Theory]
    [InlineData("title")]
    [InlineData("TITLE")]
    public void SpliceSection_CaseMismatch_ThrowsHeadingNotFound(string target)
    {
        // Arrange
        const string source = "## Title\nold\n";

        // Act
        var act = () => SectionEditor.SpliceSection(source, target, "new");

        // Assert
        var exception = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.HeadingNotFound, exception.Code);
    }

    [Fact]
    public void SpliceSection_UnknownHeading_ThrowsHeadingNotFound()
    {
        // Arrange
        const string source = "# Present\nbody\n";

        // Act
        var act = () => SectionEditor.SpliceSection(source, "Missing", "new");

        // Assert
        var exception = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.HeadingNotFound, exception.Code);
        Assert.Contains("Missing", exception.Message);
    }

    [Fact]
    public void SpliceSection_DuplicateHeadings_ThrowsAmbiguousHeading()
    {
        // Arrange
        const string source = "## Dup\na\n\n## Dup\nb\n";

        // Act
        var act = () => SectionEditor.SpliceSection(source, "Dup", "new");

        // Assert
        var exception = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.AmbiguousHeading, exception.Code);
    }

    [Fact]
    public void SpliceSection_SameTextAtDifferentLevels_ThrowsAmbiguousHeading()
    {
        // Arrange
        const string source = "# Same\na\n\n## Same\nb\n";

        // Act
        var act = () => SectionEditor.SpliceSection(source, "Same", "new");

        // Assert
        var exception = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.AmbiguousHeading, exception.Code);
    }

    [Fact]
    public void SpliceSection_HeadingWithCodeSpan_MatchesConcatenatedLiteralAndCodeText()
    {
        // Arrange
        const string source = "## The `run` command\nold\n\n## Other\nx\n";

        // Act
        var result = SectionEditor.SpliceSection(source, "The run command", "new");

        // Assert
        Assert.Equal("## The `run` command\nnew\n\n## Other\nx\n", result);
    }

    [Fact]
    public void SpliceSection_TargetWithBackticks_ThrowsHeadingNotFound()
    {
        // Arrange
        const string source = "## The `run` command\nold\n";

        // Act
        var act = () => SectionEditor.SpliceSection(source, "The `run` command", "new");

        // Assert
        var exception = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.HeadingNotFound, exception.Code);
    }

    [Fact]
    public void SpliceSection_SetextEqualsHeading_MatchedAsLevelOneTarget()
    {
        // Arrange
        const string source = "Intro\n=====\nold body\n\n# Next\ntail\n";

        // Act
        var result = SectionEditor.SpliceSection(source, "Intro", "new");

        // Assert
        Assert.Equal("Intro\nnew\n\n# Next\ntail\n", result);
    }

    [Fact]
    public void SpliceSection_SetextDashHeading_BoundsPrecedingSectionAndIsPreservedVerbatim()
    {
        // Arrange
        const string source = "## Alpha\nold\n\nBeta\n----\nkeep\n";

        // Act
        var result = SectionEditor.SpliceSection(source, "Alpha", "new");

        // Assert
        Assert.Equal("## Alpha\nnew\n\nBeta\n----\nkeep\n", result);
    }

    [Fact]
    public void SpliceSection_MultiByteContent_PreservesSurroundingTextVerbatim()
    {
        // Arrange
        const string source = "# 概要 🦝\n前文 🎉 text\n\n## ターゲット\n古い 内容\n\n## 結び ✨\n保持 🐾\n";

        // Act
        var result = SectionEditor.SpliceSection(source, "ターゲット", "新しい 本文 🚀");

        // Assert
        Assert.Equal("# 概要 🦝\n前文 🎉 text\n\n## ターゲット\n新しい 本文 🚀\n\n## 結び ✨\n保持 🐾\n", result);
    }

    [Fact]
    public void SpliceSection_EmojiHeading_MatchedAndSplicedAsLastSection()
    {
        // Arrange
        const string source = "# 概要 🦝\n前文 🎉 text\n\n## ターゲット\n古い 内容\n\n## 結び ✨\n保持 🐾\n";

        // Act
        var result = SectionEditor.SpliceSection(source, "結び ✨", "済");

        // Assert
        Assert.Equal("# 概要 🦝\n前文 🎉 text\n\n## ターゲット\n古い 内容\n\n## 結び ✨\n済\n", result);
    }
}