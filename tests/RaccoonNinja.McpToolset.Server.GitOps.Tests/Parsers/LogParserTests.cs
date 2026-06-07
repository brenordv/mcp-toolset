using System.Text;
using RaccoonNinja.McpToolset.Server.GitOps.Parsers;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Parsers;

public class LogParserTests
{
    private const char Us = '\x1f';
    private const char Rs = '\x1e';

    [Fact]
    public void Parse_Returns_Empty_For_Empty_Bytes()
    {
        Assert.Empty(LogParser.Parse(Array.Empty<byte>()));
        Assert.Empty(LogParser.Parse(null));
    }

    [Fact]
    public void Parse_Splits_Records_And_Maps_Fields()
    {
        var commit1 = $"abc123{Us}abc{Us}{Us}Alice{Us}alice@example.com{Us}1700000000{Us}1700000005{Us}subject one{Us}body one{Rs}";
        var commit2 = $"def456{Us}def{Us}abc123{Us}Bob{Us}bob@example.com{Us}1700000100{Us}1700000200{Us}subject two{Us}{Rs}";
        var bytes = Encoding.UTF8.GetBytes(commit1 + "\n" + commit2);

        var result = LogParser.Parse(bytes);

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
    public void Parse_Pads_Missing_Trailing_Fields()
    {
        var partial = $"abc{Us}a{Us}{Us}Alice{Us}alice@example.com{Us}1{Us}2{Rs}";
        var result = LogParser.Parse(Encoding.UTF8.GetBytes(partial));
        Assert.Single(result);
        Assert.Equal(string.Empty, result[0].Subject);
        Assert.Null(result[0].Body);
    }

    [Fact]
    public void Pretty_Format_Constant_Has_Expected_Fields()
    {
        Assert.Contains("%H", LogParser.LogPrettyFormat);
        Assert.Contains("%h", LogParser.LogPrettyFormat);
        Assert.Contains("%P", LogParser.LogPrettyFormat);
        Assert.Contains("%an", LogParser.LogPrettyFormat);
        Assert.Contains("%ae", LogParser.LogPrettyFormat);
        Assert.Contains("%at", LogParser.LogPrettyFormat);
        Assert.Contains("%ct", LogParser.LogPrettyFormat);
        Assert.Contains("%s", LogParser.LogPrettyFormat);
        Assert.Contains("%b", LogParser.LogPrettyFormat);
        Assert.Contains("%x1e", LogParser.LogPrettyFormat);
    }
}