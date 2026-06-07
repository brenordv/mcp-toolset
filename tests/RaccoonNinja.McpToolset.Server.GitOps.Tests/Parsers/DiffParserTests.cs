using System.Text;
using RaccoonNinja.McpToolset.Server.GitOps.Models;
using RaccoonNinja.McpToolset.Server.GitOps.Parsers;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Parsers;

public class DiffParserTests
{
    private const string WorktreeMode = "worktree";
    private const string PlainStatusPath = "src/file.cs";

    [Fact]
    public void ParseNumstatZ_Reads_Plain_Entries()
    {
        var bytes = Encoding.UTF8.GetBytes("2\t1\tsrc/file.cs\0");
        var ns = DiffParser.ParseNumstatZ(bytes);
        Assert.Equal((2, 1), ns["src/file.cs"]);
    }

    [Fact]
    public void ParseNumstatZ_Handles_Binary_Markers_As_Zero()
    {
        var bytes = Encoding.UTF8.GetBytes("-\t-\tsrc/binary.bin\0");
        var ns = DiffParser.ParseNumstatZ(bytes);
        Assert.Equal((0, 0), ns["src/binary.bin"]);
    }

    [Fact]
    public void ParseNumstatZ_Handles_Renames_With_Two_Trailing_Tokens()
    {
        var bytes = Encoding.UTF8.GetBytes("3\t0\t\0old/path.cs\0new/path.cs\0");
        var ns = DiffParser.ParseNumstatZ(bytes);
        Assert.Equal((3, 0), ns["new/path.cs"]);
    }

    [Fact]
    public void ParseUnified_Reads_Single_File_With_One_Hunk()
    {
        const string raw =
            "diff --git a/foo.txt b/foo.txt\n" +
            "index abc..def 100644\n" +
            "--- a/foo.txt\n" +
            "+++ b/foo.txt\n" +
            "@@ -1,3 +1,3 @@\n" +
            " ctx\n" +
            "-old\n" +
            "+new\n" +
            " end\n";
        var files = DiffParser.ParseUnified(Encoding.UTF8.GetBytes(raw));

        var file = Assert.Single(files);
        Assert.Equal("foo.txt", file.Path);
        Assert.Equal("foo.txt", file.OldPath);
        Assert.Equal(1, file.Additions);
        Assert.Equal(1, file.Deletions);
        var hunk = Assert.Single(file.Hunks);
        Assert.Equal(1, hunk.OldStart);
        Assert.Equal(3, hunk.OldLines);
        Assert.Equal(1, hunk.NewStart);
        Assert.Equal(3, hunk.NewLines);
        Assert.Contains("+new", hunk.Lines);
    }

    [Fact]
    public void ParseUnified_Detects_Added_And_Deleted_Files()
    {
        const string raw =
            "diff --git a/new.txt b/new.txt\n" +
            "new file mode 100644\n" +
            "--- /dev/null\n" +
            "+++ b/new.txt\n" +
            "@@ -0,0 +1,1 @@\n" +
            "+hello\n" +
            "diff --git a/gone.txt b/gone.txt\n" +
            "deleted file mode 100644\n";
        var files = DiffParser.ParseUnified(Encoding.UTF8.GetBytes(raw));

        Assert.Equal(2, files.Count);
        Assert.Equal(ChangeType.Added, files[0].ChangeType);
        Assert.Equal(ChangeType.Deleted, files[1].ChangeType);
    }

    [Fact]
    public void ParseUnified_Flags_Binary_Files()
    {
        const string raw =
            "diff --git a/img.png b/img.png\n" +
            "Binary files a/img.png and b/img.png differ\n";
        var files = DiffParser.ParseUnified(Encoding.UTF8.GetBytes(raw));
        Assert.True(files.Single().IsBinary);
    }

    [Fact]
    public void AssembleDiffResult_Enriches_From_Numstat()
    {
        var input = new List<FileDiff>
        {
            new() { Path = "foo.txt", Additions = 0, Deletions = 0 },
        };
        var numstat = new Dictionary<string, (int Additions, int Deletions)>
        {
            ["foo.txt"] = (5, 2),
        };
        var result = DiffParser.AssembleDiffResult(input, WorktreeMode, numstat: numstat);

        Assert.Equal(5, result.Files[0].Additions);
        Assert.Equal(2, result.Files[0].Deletions);
        Assert.Equal(5, result.TotalAdditions);
        Assert.Equal(2, result.TotalDeletions);
        Assert.Equal(WorktreeMode, result.Mode);
    }

    [Fact]
    public void AssembleDiffResult_Without_Numstat_Sums_Provided_Counts()
    {
        var input = new List<FileDiff>
        {
            new() { Path = "a.cs", Additions = 2, Deletions = 1 },
            new() { Path = "b.cs", Additions = 3, Deletions = 4 },
        };
        var result = DiffParser.AssembleDiffResult(input, WorktreeMode);

        Assert.Equal(2, result.Files.Count);
        Assert.Equal(5, result.TotalAdditions);
        Assert.Equal(5, result.TotalDeletions);
        Assert.Equal(WorktreeMode, result.Mode);
        Assert.False(result.Truncated);
    }

    [Theory]
    [MemberData(nameof(RenameAndCopyDiffs))]
    public void ParseUnified_Sets_ChangeType_And_OldPath_For_Renames_And_Copies(
        string raw, ChangeType expectedChange, string expectedOldPath, string expectedPath)
    {
        var files = DiffParser.ParseUnified(Encoding.UTF8.GetBytes(raw));

        var file = Assert.Single(files);
        Assert.Equal(expectedChange, file.ChangeType);
        Assert.Equal(expectedOldPath, file.OldPath);
        Assert.Equal(expectedPath, file.Path);
    }

    [Theory]
    [InlineData(ChangeType.Modified, "modified")]
    [InlineData(ChangeType.Added, "added")]
    [InlineData(ChangeType.Deleted, "deleted")]
    [InlineData(ChangeType.Renamed, "renamed")]
    [InlineData(ChangeType.Copied, "copied")]
    [InlineData(ChangeType.Unknown, "unknown")]
    public void ChangeType_Serializes_As_Lowercase_String(ChangeType value, string expected)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new FileDiff { Path = "x", ChangeType = value });

        Assert.Contains($"\"change_type\":\"{expected}\"", json);
    }

    [Fact]
    public void ChangeType_RoundTrips_Through_Json()
    {
        var original = new FileDiff { Path = "x", ChangeType = ChangeType.Renamed };
        var json = System.Text.Json.JsonSerializer.Serialize(original);

        var restored = System.Text.Json.JsonSerializer.Deserialize<FileDiff>(json);

        Assert.Equal(ChangeType.Renamed, restored.ChangeType);
    }

    [Theory]
    [MemberData(nameof(PlainNameStatusEntries))]
    public void ParseNameStatusZ_Maps_Plain_Status_Letters(string raw, ChangeType expected)
    {
        var result = DiffParser.ParseNameStatusZ(Encoding.UTF8.GetBytes(raw));
        Assert.Equal(expected, result[PlainStatusPath]);
    }

    [Fact]
    public void ParseNameStatusZ_Keys_Rename_On_New_Path()
    {
        const string oldPath = "old/path.cs";
        const string newPath = "new/path.cs";
        var bytes = Encoding.UTF8.GetBytes($"R100\0{oldPath}\0{newPath}\0");

        var result = DiffParser.ParseNameStatusZ(bytes);

        Assert.Equal(ChangeType.Renamed, result[newPath]);
        Assert.False(result.ContainsKey(oldPath));
    }

    [Fact]
    public void ParseNameStatusZ_Keys_Copy_On_New_Path()
    {
        const string origPath = "orig.cs";
        const string copyPath = "copy.cs";
        var bytes = Encoding.UTF8.GetBytes($"C075\0{origPath}\0{copyPath}\0");

        var result = DiffParser.ParseNameStatusZ(bytes);

        Assert.Equal(ChangeType.Copied, result[copyPath]);
        Assert.False(result.ContainsKey(origPath));
    }

    [Fact]
    public void ParseNameStatusZ_Returns_Empty_For_Empty_Input()
    {
        var result = DiffParser.ParseNameStatusZ(Array.Empty<byte>());
        Assert.Empty(result);
    }

    [Fact]
    public void ParseNameStatusZ_Ignores_Trailing_Empty_Token()
    {
        // The trailing NUL yields an empty token after the last path; it must not be treated as a record.
        var result = DiffParser.ParseNameStatusZ(Encoding.UTF8.GetBytes($"M\0{PlainStatusPath}\0"));

        Assert.Equal(ChangeType.Modified, Assert.Single(result).Value);
    }

    [Fact]
    public void AssembleDiffResult_Overlays_ChangeType_When_Unknown()
    {
        var input = new List<FileDiff>
        {
            new() { Path = "foo.cs", ChangeType = ChangeType.Unknown },
        };
        var changeTypes = new Dictionary<string, ChangeType>
        {
            ["foo.cs"] = ChangeType.Added,
        };

        var result = DiffParser.AssembleDiffResult(input, WorktreeMode, changeTypes: changeTypes);

        Assert.Equal(ChangeType.Added, result.Files[0].ChangeType);
    }

    [Fact]
    public void AssembleDiffResult_Does_Not_Clobber_Existing_ChangeType()
    {
        var input = new List<FileDiff>
        {
            new() { Path = "foo.cs", ChangeType = ChangeType.Modified },
        };
        var changeTypes = new Dictionary<string, ChangeType>
        {
            ["foo.cs"] = ChangeType.Added,
        };

        var result = DiffParser.AssembleDiffResult(input, WorktreeMode, changeTypes: changeTypes);

        Assert.Equal(ChangeType.Modified, result.Files[0].ChangeType);
    }

    #region Test Helpers

    public static TheoryData<string, ChangeType> PlainNameStatusEntries()
    {
        return new()
        {
            { $"A\0{PlainStatusPath}\0", ChangeType.Added },
            { $"M\0{PlainStatusPath}\0", ChangeType.Modified },
            { $"D\0{PlainStatusPath}\0", ChangeType.Deleted },
            { $"T\0{PlainStatusPath}\0", ChangeType.Modified },
            { $"U\0{PlainStatusPath}\0", ChangeType.Unknown },
            { $"X\0{PlainStatusPath}\0", ChangeType.Unknown },
        };
    }

    public static TheoryData<string, ChangeType, string, string> RenameAndCopyDiffs()
    {
        return new()
        {
            {
                "diff --git a/old.txt b/new.txt\n" +
                "similarity index 95%\n" +
                "rename from old.txt\n" +
                "rename to new.txt\n",
                ChangeType.Renamed,
                "old.txt",
                "new.txt"
            },
            {
                "diff --git a/orig.txt b/copy.txt\n" +
                "similarity index 100%\n" +
                "copy from orig.txt\n" +
                "copy to copy.txt\n",
                ChangeType.Copied,
                "orig.txt",
                "copy.txt"
            },
        };
    }

    #endregion
}