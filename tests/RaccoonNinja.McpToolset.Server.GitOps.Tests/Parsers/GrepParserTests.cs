using System.Text;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Models;
using RaccoonNinja.McpToolset.Server.GitOps.Parsers;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Parsers;

public sealed class GrepParserTests
{
    private const string NotWholeRecordsMessage = "git printed output that is not a whole number of NUL-framed records";
    private const string Nul = "\0";
    private const string Newline = "\n";
    private const string NewlinePath = "x\nREADME.md";
    private const string FirstPath = "a.txt";
    private const string SecondPath = "b.txt";
    private const string FirstCountPath = "a.cs";
    private const string Needle = "needle";
    private const string Hit = "hit";
    private const string CutLine = "cu";
    private const string NotANumber = "x";
    private const string One = "1";
    private const string Two = "2";
    private const string Three = "3";
    private const string WholeMatch = FirstPath + Nul + One + Nul + "whole" + Newline;
    private const string WholeCount = FirstCountPath + Nul + Three + Newline;
    private const int SecondLine = 2;
    private const int FirstCount = 3;

    [Fact]
    public void Parse_EmptyOutput_ReturnsNoMatches()
    {
        // Act
        var matches = GrepParser.Parse([], null, truncated: false);

        // Assert
        Assert.Empty(matches);
    }

    [Fact]
    public void Parse_PathHoldingNewline_ReturnsOneMatchWithTheWholePath()
    {
        // Arrange
        const string input = NewlinePath + Nul + One + Nul + Needle + Newline;

        // Act
        var matches = GrepParser.Parse(Encoding.UTF8.GetBytes(input), null, truncated: false);

        // Assert
        var match = Assert.Single(matches);
        Assert.Equal(new GrepMatch { Path = NewlinePath, Line = 1, Content = Needle }, match);
    }

    [Fact]
    public void Parse_LineHoldingNul_KeepsTheWholeLine()
    {
        // Arrange
        const string lineHoldingNul = Needle + Nul + "tail";
        const string input = FirstPath + Nul + Two + Nul + lineHoldingNul + Newline;

        // Act
        var matches = GrepParser.Parse(Encoding.UTF8.GetBytes(input), null, truncated: false);

        // Assert
        var match = Assert.Single(matches);
        Assert.Equal(lineHoldingNul, match.Content);
    }

    [Fact]
    public void Parse_LineEndingWithCarriageReturn_TrimsIt()
    {
        // Arrange
        const string input = FirstPath + Nul + One + Nul + Hit + "\r" + Newline;

        // Act
        var matches = GrepParser.Parse(Encoding.UTF8.GetBytes(input), null, truncated: false);

        // Assert
        var match = Assert.Single(matches);
        Assert.Equal(Hit, match.Content);
    }

    [Theory]
    [InlineData(FirstPath + Nul + One + Nul + "no newline", false)]
    [InlineData(FirstPath + Nul + NotANumber + Nul + Hit + Newline, false)]
    [InlineData(FirstPath + Nul + Nul + Hit + Newline, false)]
    [InlineData(Nul + One + Nul + Hit + Newline, false)]
    [InlineData("Binary file " + FirstPath + " matches" + Newline, false)]
    [InlineData(FirstPath + Nul + NotANumber + Nul + Hit + Newline + SecondPath + Nul + One + Nul + CutLine, true)]
    public void Parse_OutputNotWholeMatches_Throws(string input, bool truncated)
    {
        // Act
        var act = () => GrepParser.Parse(Encoding.UTF8.GetBytes(input), null, truncated);

        // Assert
        Assert.Equal(NotWholeRecordsMessage, Assert.Throws<GitCommandException>(act).Message);
    }

    [Fact]
    public void Parse_TruncatedInsideTheLine_ReturnsTheCutMatch()
    {
        // Arrange
        const string input = WholeMatch + SecondPath + Nul + Two + Nul + CutLine;

        // Act
        var matches = GrepParser.Parse(Encoding.UTF8.GetBytes(input), null, truncated: true);

        // Assert
        Assert.Equal(2, matches.Count);
        Assert.Equal(new GrepMatch { Path = SecondPath, Line = SecondLine, Content = CutLine }, matches[1]);
    }

    [Fact]
    public void Parse_TruncatedRightAfterTheLineNumber_ReturnsAnEmptyLine()
    {
        // Arrange
        const string input = WholeMatch + SecondPath + Nul + Two + Nul;

        // Act
        var matches = GrepParser.Parse(Encoding.UTF8.GetBytes(input), null, truncated: true);

        // Assert
        Assert.Equal(2, matches.Count);
        Assert.Equal(new GrepMatch { Path = SecondPath, Line = SecondLine, Content = string.Empty }, matches[1]);
    }

    [Theory]
    [InlineData(WholeMatch + "b.t")]
    [InlineData(WholeMatch + SecondPath + Nul + One + Two)]
    public void Parse_TruncatedInsideThePathOrLineNumber_LeavesTheMatchOut(string input)
    {
        // Act
        var matches = GrepParser.Parse(Encoding.UTF8.GetBytes(input), null, truncated: true);

        // Assert
        var match = Assert.Single(matches);
        Assert.Equal(FirstPath, match.Path);
    }

    [Fact]
    public void ParseCounts_PathHoldingNewline_ReturnsOneCountWithTheWholePath()
    {
        // Arrange
        const string input = NewlinePath + Nul + One + Newline;

        // Act
        var counts = GrepParser.ParseCounts(Encoding.UTF8.GetBytes(input), null, truncated: false);

        // Assert
        var count = Assert.Single(counts);
        Assert.Equal(new GrepFileCount { Path = NewlinePath, MatchCount = 1 }, count);
    }

    [Theory]
    [InlineData(FirstCountPath + Nul + Three)]
    [InlineData(Nul + Three + Newline)]
    public void ParseCounts_OutputNotWholeCounts_Throws(string input)
    {
        // Act
        var act = () => GrepParser.ParseCounts(Encoding.UTF8.GetBytes(input), null, truncated: false);

        // Assert
        Assert.Equal(NotWholeRecordsMessage, Assert.Throws<GitCommandException>(act).Message);
    }

    [Fact]
    public void ParseCounts_TruncatedInsideTheCount_LeavesTheCountOut()
    {
        // Arrange
        const string input = WholeCount + "b.cs" + Nul + One;

        // Act
        var counts = GrepParser.ParseCounts(Encoding.UTF8.GetBytes(input), null, truncated: true);

        // Assert
        var count = Assert.Single(counts);
        Assert.Equal(new GrepFileCount { Path = FirstCountPath, MatchCount = FirstCount }, count);
    }

    [Fact]
    public void ParseCounts_TruncatedInsideThePath_LeavesTheCountOut()
    {
        // Arrange
        const string input = WholeCount + "b.c";

        // Act
        var counts = GrepParser.ParseCounts(Encoding.UTF8.GetBytes(input), null, truncated: true);

        // Assert
        var count = Assert.Single(counts);
        Assert.Equal(new GrepFileCount { Path = FirstCountPath, MatchCount = FirstCount }, count);
    }
}