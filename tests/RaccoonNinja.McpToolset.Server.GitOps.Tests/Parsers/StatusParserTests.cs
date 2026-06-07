using System.Text;
using RaccoonNinja.McpToolset.Server.GitOps.Parsers;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Parsers;

public class StatusParserTests
{
    [Fact]
    public void Parse_Reads_Branch_Headers_And_AheadBehind()
    {
        var bytes = Encoding.UTF8.GetBytes(
            "# branch.head main\0# branch.upstream origin/main\0# branch.ab +2 -1\0");
        var status = StatusParser.Parse(bytes);

        Assert.Equal("main", status.Branch);
        Assert.Equal("origin/main", status.Upstream);
        Assert.Equal(2, status.Ahead);
        Assert.Equal(1, status.Behind);
        Assert.True(status.IsClean);
    }

    [Fact]
    public void Parse_Marks_Branch_Null_When_Detached()
    {
        var bytes = Encoding.UTF8.GetBytes("# branch.head (detached)\0");
        Assert.Null(StatusParser.Parse(bytes).Branch);
    }

    [Fact]
    public void Parse_Classifies_Staged_Unstaged_Untracked()
    {
        var bytes = Encoding.UTF8.GetBytes(
            "1 M. N... 100644 100644 100644 abc abc src/staged.txt\0" +
            "1 .M N... 100644 100644 100644 def def src/unstaged.txt\0" +
            "? src/new.txt\0");
        var status = StatusParser.Parse(bytes);

        Assert.Single(status.Staged, s => s.Path == "src/staged.txt");
        Assert.Single(status.Unstaged, s => s.Path == "src/unstaged.txt");
        Assert.Single(status.Untracked, s => s.Path == "src/new.txt" && s.IsUntracked);
        Assert.False(status.IsClean);
    }

    [Fact]
    public void Parse_Handles_Rename_With_Orig_Path_In_Next_Token()
    {
        var bytes = Encoding.UTF8.GetBytes(
            "2 R. N... 100644 100644 100644 abc abc R100 src/new.txt\0src/old.txt\0");
        var status = StatusParser.Parse(bytes);

        var entry = Assert.Single(status.Staged);
        Assert.Equal("src/new.txt", entry.Path);
        Assert.Equal("src/old.txt", entry.OrigPath);
        Assert.True(entry.IsRenamed);
    }
}