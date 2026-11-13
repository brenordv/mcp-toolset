using System.Text;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Models;
using RaccoonNinja.McpToolset.Server.GitOps.Parsers;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Parsers;

public class BlameParserTests
{
    private const string FirstCommit = "1111111111111111111111111111111111111111";
    private const string SecondCommit = "2222222222222222222222222222222222222222";
    private const string ThirdCommit = "3333333333333333333333333333333333333333";
    private const string UncommittedCommit = "0000000000000000000000000000000000000000";
    private const string FirstAuthor = "Alice";
    private const string SecondAuthor = "Bob";
    private const string MappedAuthor = "Mapped";
    private const string UncommittedAuthor = "Not Committed Yet";
    private const string FirstRecord = $"{FirstCommit}\0{FirstAuthor}\0";
    private const string SecondRecord = $"{SecondCommit}\0{SecondAuthor}\0";

    [Fact]
    public void Parse_ReadsShaLineNumberAuthorAndContent()
    {
        // Arrange
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

        // Act
        var lines = BlameParser.Parse(Encoding.UTF8.GetBytes(raw));

        // Assert
        Assert.Equal(2, lines.Count);
        Assert.Equal(1, lines[0].LineNo);
        Assert.Equal("hello", lines[0].Content);
        Assert.Equal("Alice", lines[0].AuthorName);
        Assert.Equal(1700000000, lines[0].AuthoredAt.ToUnixTimeSeconds());
        Assert.Equal("world", lines[1].Content);
    }

    [Fact]
    public void Parse_SkipsNonShaHeaders()
    {
        // Arrange
        const string raw = "boundary\n\nnot-a-sha 1 1\n";

        // Act & Assert
        Assert.Empty(BlameParser.Parse(Encoding.UTF8.GetBytes(raw)));
    }

    [Fact]
    public void ParseRawAuthors_ReadsEachRequestedCommitsAuthor()
    {
        // Arrange
        var requested = new HashSet<string>(StringComparer.Ordinal) { FirstCommit, SecondCommit };
        var bytes = Encoding.UTF8.GetBytes(FirstRecord + SecondRecord);

        // Act
        var authors = BlameParser.ParseRawAuthors(bytes, requested);

        // Assert
        Assert.Equal(2, authors.Count);
        Assert.Equal(FirstAuthor, authors[FirstCommit]);
        Assert.Equal(SecondAuthor, authors[SecondCommit]);
    }

    [Theory]
    [MemberData(nameof(MismatchedAuthorRecords))]
    public void ParseRawAuthors_RecordsNotMatchingTheRequest_Throws(string raw)
    {
        // Arrange
        var requested = new HashSet<string>(StringComparer.Ordinal) { FirstCommit, SecondCommit };
        var bytes = Encoding.UTF8.GetBytes(raw);

        // Act
        var act = () => BlameParser.ParseRawAuthors(bytes, requested);

        // Assert
        Assert.Throws<GitCommandException>(act);
    }

    [Fact]
    public void WithRawAuthors_SetsMailmapNameOnlyWhereItDiffers()
    {
        // Arrange
        var lines = new[]
        {
            new BlameLine { LineNo = 1, CommitHash = FirstCommit, AuthorName = MappedAuthor },
            new BlameLine { LineNo = 2, CommitHash = SecondCommit, AuthorName = SecondAuthor },
            new BlameLine { LineNo = 3, CommitHash = UncommittedCommit, AuthorName = UncommittedAuthor },
        };
        var rawAuthors = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FirstCommit] = FirstAuthor,
            [SecondCommit] = SecondAuthor,
        };

        // Act
        var result = BlameParser.WithRawAuthors(lines, rawAuthors);

        // Assert
        Assert.Equal(FirstAuthor, result[0].AuthorName);
        Assert.Equal(MappedAuthor, result[0].MailmapAuthorName);
        Assert.Equal(SecondAuthor, result[1].AuthorName);
        Assert.Null(result[1].MailmapAuthorName);
        Assert.Equal(UncommittedAuthor, result[2].AuthorName);
        Assert.Null(result[2].MailmapAuthorName);
    }

    #region Test Helpers

    public static TheoryData<string> MismatchedAuthorRecords() => new()
    {
        FirstRecord,
        FirstRecord + FirstRecord,
        FirstRecord + $"{ThirdCommit}\0{SecondAuthor}\0",
        FirstRecord + $"{SecondCommit}\0{SecondAuthor}",
    };

    #endregion
}