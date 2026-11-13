using System.Text;
using RaccoonNinja.McpToolset.Server.GitOps.Parsers;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Parsers;

public class LogParserTests
{
    private const string Nul = "\0";
    private const string FirstRecord = $"abc123{Nul}abc{Nul}{Nul}Alice{Nul}alice@example.com{Nul}1700000000{Nul}1700000005{Nul}subject one{Nul}body one{Nul}";

    [Fact]
    public void Parse_ReturnsEmptyForEmptyBytes()
    {
        // Act & Assert
        Assert.Empty(LogParser.Parse(Array.Empty<byte>(), truncated: false));
        Assert.Empty(LogParser.Parse(null, truncated: false));
    }

    [Fact]
    public void Parse_SplitsRecordsAndMapsFields()
    {
        // Arrange
        const string commit2 = $"def456{Nul}def{Nul}abc123{Nul}Bob{Nul}bob@example.com{Nul}1700000100{Nul}1700000200{Nul}subject two{Nul}{Nul}";
        var bytes = Encoding.UTF8.GetBytes(FirstRecord + commit2);

        // Act
        var result = LogParser.Parse(bytes, truncated: false);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("abc123", result[0].Hash);
        Assert.Equal("abc", result[0].ShortHash);
        Assert.Empty(result[0].Parents);
        Assert.Equal("Alice", result[0].AuthorName);
        Assert.Equal("alice@example.com", result[0].AuthorEmail);
        Assert.Equal("subject one", result[0].Subject);
        Assert.Equal("body one", result[0].Body);
        Assert.Equal(1700000000, result[0].AuthoredAt.ToUnixTimeSeconds());

        Assert.Equal("def456", result[1].Hash);
        Assert.Single(result[1].Parents, "abc123");
        Assert.Null(result[1].Body);
    }

    [Fact]
    public void Parse_TruncatedInsideTheShortHash_LeavesTheCutRecordOut()
    {
        // Arrange
        const string output = FirstRecord + $"def456{Nul}de";

        // Act
        var result = LogParser.Parse(Encoding.UTF8.GetBytes(output), truncated: true);

        // Assert
        var commit = Assert.Single(result);
        Assert.Equal("abc123", commit.Hash);
    }

    [Fact]
    public void Parse_TruncatedInsideTheSubject_PadsTheCutRecord()
    {
        // Arrange
        const string output = FirstRecord + $"def456{Nul}def{Nul}abc123{Nul}Bob{Nul}bob@example.com{Nul}1700000100{Nul}1700000200{Nul}subj";

        // Act
        var result = LogParser.Parse(Encoding.UTF8.GetBytes(output), truncated: true);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("def456", result[1].Hash);
        Assert.Equal("subj", result[1].Subject);
        Assert.Null(result[1].Body);
    }

    [Fact]
    public void Pretty_FormatConstantHasExpectedFields()
    {
        // Act & Assert
        Assert.Contains("%H", LogParser.LogPrettyFormat);
        Assert.Contains("%h", LogParser.LogPrettyFormat);
        Assert.Contains("%P", LogParser.LogPrettyFormat);
        Assert.Contains("%an", LogParser.LogPrettyFormat);
        Assert.Contains("%ae", LogParser.LogPrettyFormat);
        Assert.Contains("%at", LogParser.LogPrettyFormat);
        Assert.Contains("%ct", LogParser.LogPrettyFormat);
        Assert.Contains("%s", LogParser.LogPrettyFormat);
        Assert.Contains("%b", LogParser.LogPrettyFormat);
        Assert.Contains("%x00", LogParser.LogPrettyFormat);
        Assert.Equal(["-z", $"--pretty=tformat:{LogParser.LogPrettyFormat}"], LogParser.FormatFlags);
    }
}