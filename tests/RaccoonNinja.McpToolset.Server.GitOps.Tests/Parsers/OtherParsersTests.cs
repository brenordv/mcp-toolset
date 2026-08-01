using System.Text;
using RaccoonNinja.McpToolset.Server.GitOps.Parsers;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Parsers;

public class OtherParsersTests
{
    //  is the Unit Separator. We avoid the variable-width \x escape because
    // C# greedily consumes up to 4 hex digits, which can swallow neighbouring
    // characters when the next char is also a hex digit.
    private const string Us = "";

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