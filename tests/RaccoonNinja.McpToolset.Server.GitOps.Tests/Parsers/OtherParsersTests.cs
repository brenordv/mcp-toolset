using System.Text;
using RaccoonNinja.McpToolset.Server.GitOps.Models;
using RaccoonNinja.McpToolset.Server.GitOps.Parsers;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Parsers;

public class OtherParsersTests
{
    //  is the Unit Separator. We avoid the variable-width \x escape because
    // C# greedily consumes up to 4 hex digits, which can swallow neighbouring
    // characters when the next char is also a hex digit.
    private const string Us = "";

    private const string ObjectIdA = "1111111111111111111111111111111111111111";
    private const string ObjectIdB = "2222222222222222222222222222222222222222";
    private const string ObjectIdC = "3333333333333333333333333333333333333333";

    private static readonly string[] LsFilesExpected = ["a.txt", "src/b.cs", "c"];

    [Fact]
    public void LsFiles_SplitsOnNulAndDropsEmpties()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes("a.txt\0src/b.cs\0\0c\0");

        // Act
        var result = LsFilesParser.Parse(bytes);

        // Assert
        Assert.Equal(LsFilesExpected, result);
    }

    [Fact]
    public void LsFilesStage_ParsesModeObjectStageAndPath()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes($"100644 {ObjectIdA} 0\tsrc/a.cs\0100755 {ObjectIdB} 0\tscripts/run.sh\0");

        // Act
        var result = LsFilesParser.ParseStage(bytes);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("src/a.cs", result[0].Path);
        Assert.Equal("100644", result[0].Mode);
        Assert.Equal(ObjectIdA, result[0].ObjectId);
        Assert.Equal(0, result[0].Stage);
        Assert.Equal("scripts/run.sh", result[1].Path);
        Assert.Equal("100755", result[1].Mode);
        Assert.Equal(ObjectIdB, result[1].ObjectId);
    }

    [Fact]
    public void LsFilesStage_SkipsMalformedRecords()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes("junk\0100644 abc 0\tok.txt\0");

        // Act
        var result = LsFilesParser.ParseStage(bytes);

        // Assert
        var entry = Assert.Single(result);
        Assert.Equal("ok.txt", entry.Path);
    }

    [Fact]
    public void LsFilesStage_UnmergedEntries_KeepEveryStage()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes(
            $"100644 {ObjectIdA} 1\tc.txt\0100644 {ObjectIdB} 2\tc.txt\0100644 {ObjectIdC} 3\tc.txt\0");

        // Act
        var result = LsFilesParser.ParseStage(bytes);

        // Assert
        Assert.Equal([1, 2, 3], result.Select(entry => entry.Stage));
        Assert.All(result, entry => Assert.Equal("c.txt", entry.Path));
    }

    [Fact]
    public void LsFilesStage_EmptyInput_ReturnsEmpty()
    {
        // Arrange
        var bytes = Array.Empty<byte>();

        // Act
        var result = LsFilesParser.ParseStage(bytes);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void IsLikelyBinary_NulInFirst8000Bytes_IsTrue()
    {
        // Arrange
        byte[] bytes = [0x89, 0x50, 0x00, 0x0A];

        // Act
        var actual = TextDecoding.IsLikelyBinary(bytes);

        // Assert
        Assert.True(actual);
    }

    [Fact]
    public void IsLikelyBinary_NulAfter8000Bytes_IsFalse()
    {
        // Arrange
        var bytes = new byte[8001];
        Array.Fill(bytes, (byte)'a', 0, 8000);

        // Act
        var actual = TextDecoding.IsLikelyBinary(bytes);

        // Assert
        Assert.False(actual);
    }

    [Fact]
    public void IsLikelyBinary_EmptyOrText_IsFalse()
    {
        // Arrange
        var text = Encoding.UTF8.GetBytes("plain text\n");

        // Act & Assert
        Assert.False(TextDecoding.IsLikelyBinary([]));
        Assert.False(TextDecoding.IsLikelyBinary(null));
        Assert.False(TextDecoding.IsLikelyBinary(text));
    }

    [Fact]
    public void Branch_StripsRefsHeadsAndDetectsCurrent()
    {
        // Arrange
        var line = $"refs/heads/main{Us}*{Us}abc123{Us}origin/main{Us}tip subject\n" +
                   $"refs/remotes/origin/main{Us} {Us}abc123{Us}{Us}\n";

        // Act
        var branches = BranchParser.Parse(Encoding.UTF8.GetBytes(line));

        // Assert
        Assert.Equal(2, branches.Count);
        Assert.Equal("main", branches[0].Name);
        Assert.True(branches[0].IsCurrent);
        Assert.False(branches[0].IsRemote);
        Assert.Equal("origin/main", branches[0].Upstream);
        Assert.True(branches[1].IsRemote);
    }

    [Fact]
    public void Stash_ParsesIndexFromRefname()
    {
        // Arrange
        var line = $"stash@{{2}}{Us}On main: WIP{Us}1700000000";

        // Act
        var entries = StashListParser.Parse(Encoding.UTF8.GetBytes(line));

        // Assert
        var entry = Assert.Single(entries);
        Assert.Equal(2, entry.Index);
        Assert.Equal("On main: WIP", entry.Subject);
    }

    [Fact]
    public void Reflog_ParsesFieldsAndEpochIntoStructuredWhen()
    {
        // Arrange
        var line = $"abc123def{Us}abc123{Us}HEAD@{{0}}{Us}commit: init{Us}1700000000";

        // Act
        var entries = ReflogParser.Parse(Encoding.UTF8.GetBytes(line));

        // Assert
        var entry = Assert.Single(entries);
        Assert.Equal("abc123def", entry.Hash);
        Assert.Equal("abc123", entry.ShortHash);
        Assert.Equal("HEAD@{0}", entry.Selector);
        Assert.Equal("commit: init", entry.Subject);
        Assert.Equal(System.DateTimeOffset.FromUnixTimeSeconds(1700000000), entry.When);
        Assert.False(string.IsNullOrWhiteSpace(entry.RelativeTime));
    }

    [Fact]
    public void Grep_ParsesPathLineAndContent()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes("alpha.txt\09\0the alpha line");

        // Act
        var matches = GrepParser.Parse(bytes, null);

        // Assert
        var match = Assert.Single(matches);
        Assert.Equal("alpha.txt", match.Path);
        Assert.Equal(9, match.Line);
        Assert.Equal("the alpha line", match.Content);
    }

    [Fact]
    public void Grep_StripsRefPrefixFromPathWhenRefSupplied()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes("HEAD:alpha.txt\03\0content");

        // Act
        var matches = GrepParser.Parse(bytes, "HEAD");

        // Assert
        var match = Assert.Single(matches);
        Assert.Equal("alpha.txt", match.Path);
        Assert.Equal(3, match.Line);
    }

    [Fact]
    public void ParseCounts_NulSeparatedRecords_ReturnsPathAndCount()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes("a.cs\03\nb/c.cs\01\n");

        // Act
        var counts = GrepParser.ParseCounts(bytes, null);

        // Assert
        Assert.Collection(
            counts,
            count => Assert.Equal(new GrepFileCount { Path = "a.cs", MatchCount = 3 }, count),
            count => Assert.Equal(new GrepFileCount { Path = "b/c.cs", MatchCount = 1 }, count));
    }

    [Fact]
    public void ParseCounts_WithRef_StripsRefPrefix()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes("abc123:dir/x.cs\02\n");

        // Act
        var counts = GrepParser.ParseCounts(bytes, "abc123");

        // Assert
        var count = Assert.Single(counts);
        Assert.Equal("dir/x.cs", count.Path);
        Assert.Equal(2, count.MatchCount);
    }

    [Fact]
    public void ParseCounts_RecordWithoutNul_IsSkipped()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes("a.cs:4\n");

        // Act
        var counts = GrepParser.ParseCounts(bytes, null);

        // Assert
        Assert.Empty(counts);
    }

    [Fact]
    public void ParseCounts_MalformedCount_SkipsRecord()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes("a.cs\0x\n");

        // Act
        var counts = GrepParser.ParseCounts(bytes, null);

        // Assert
        Assert.Empty(counts);
    }

    [Fact]
    public void ParseCounts_Empty_ReturnsEmpty()
    {
        // Act
        var counts = GrepParser.ParseCounts([], null);

        // Assert
        Assert.Empty(counts);
    }

    [Fact]
    public void RelativeTime_ReturnsSingularAndPluralUnits()
    {
        // Arrange
        var now = System.DateTimeOffset.UtcNow;

        // Act & Assert
        Assert.Equal("1 minute ago", global::RaccoonNinja.McpToolset.Server.GitOps.Parsers.RelativeTime.Describe(now.AddMinutes(-1), now));
        Assert.Equal("2 hours ago", global::RaccoonNinja.McpToolset.Server.GitOps.Parsers.RelativeTime.Describe(now.AddHours(-2), now));
        Assert.StartsWith("in ", global::RaccoonNinja.McpToolset.Server.GitOps.Parsers.RelativeTime.Describe(now.AddMinutes(5), now));
    }
}