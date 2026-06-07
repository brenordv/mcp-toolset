using System.Text;
using RaccoonNinja.McpToolset.Server.GitOps.Parsers;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Parsers;

public class BlameParserTests
{
    [Fact]
    public void Parse_Reads_Sha_Line_Number_Author_And_Content()
    {
        const string raw =
            "1111111111111111111111111111111111111111 1 1 1\n" +
            "author Alice\n" +
            "author-mail <alice@example.com>\n" +
            "author-time 1700000000\n" +
            "author-tz +0000\n" +
            "summary first\n" +
            "filename foo.txt\n" +
            "\thello\n" +
            "1111111111111111111111111111111111111111 2 2\n" +
            "\tworld\n";
        var lines = BlameParser.Parse(Encoding.UTF8.GetBytes(raw));

        Assert.Equal(2, lines.Count);
        Assert.Equal(1, lines[0].LineNo);
        Assert.Equal("hello", lines[0].Content);
        Assert.Equal("Alice", lines[0].AuthorName);
        Assert.Equal(1700000000, lines[0].AuthoredAt.ToUnixTimeSeconds());
        Assert.Equal("world", lines[1].Content);
    }

    [Fact]
    public void Parse_Skips_Non_Sha_Headers()
    {
        const string raw = "boundary\n\nnot-a-sha 1 1\n";
        Assert.Empty(BlameParser.Parse(Encoding.UTF8.GetBytes(raw)));
    }
}