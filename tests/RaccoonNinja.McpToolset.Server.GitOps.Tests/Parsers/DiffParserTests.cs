using System.Text;
using RaccoonNinja.McpToolset.Server.GitOps.Models;
using RaccoonNinja.McpToolset.Server.GitOps.Parsers;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Parsers;

public class DiffParserTests
{
    private const string WorktreeMode = "worktree";
    private const string PlainStatusPath = "src/file.cs";
    private const string RealFile = "real.txt";
    private const string RealHunkHeader = "@@ -1 +1 @@";

    private const string RealFileSection =
        "diff --git a/real.txt b/real.txt\n" +
        "index 1111111..2222222 100644\n" +
        "--- a/real.txt\n" +
        "+++ b/real.txt\n" +
        RealHunkHeader + "\n" +
        "-old\n" +
        "+new\n";

    private const string CombinedSectionBody =
        "index 3333333,4444444..0000000\n" +
        "--- a/m.txt\n" +
        "+++ b/m.txt\n" +
        "@@@ -1,1 -1,1 +1,5 @@@\n" +
        "++<<<<<<< HEAD\n" +
        " +ours\n" +
        "++=======\n" +
        "+ theirs\n" +
        "++>>>>>>> theirs\n";

    private const string ForgedUnmergedPath = "m\ndiff --git a/fake.txt b/fake.txt";
    private const string UnmergedLinePrefix = "* Unmerged path ";

    [Fact]
    public void ParseNumstatZ_ReadsPlainEntries()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes("2\t1\tsrc/file.cs\0");

        // Act
        var ns = DiffParser.ParseNumstatZ(bytes);

        // Assert
        Assert.Equal((2, 1), ns["src/file.cs"]);
    }

    [Fact]
    public void ParseNumstatZ_HandlesBinaryMarkersAsZero()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes("-\t-\tsrc/binary.bin\0");

        // Act
        var ns = DiffParser.ParseNumstatZ(bytes);

        // Assert
        Assert.Equal((0, 0), ns["src/binary.bin"]);
    }

    [Fact]
    public void ParseNumstatZ_HandlesRenamesWithTwoTrailingTokens()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes("3\t0\t\0old/path.cs\0new/path.cs\0");

        // Act
        var ns = DiffParser.ParseNumstatZ(bytes);

        // Assert
        Assert.Equal((3, 0), ns["new/path.cs"]);
    }

    [Fact]
    public void ParseNumstatZ_PathWithTab_KeepsWholePath()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes("1\t0\ta\tb.txt\0");

        // Act
        var ns = DiffParser.ParseNumstatZ(bytes);

        // Assert
        var entry = Assert.Single(ns);
        Assert.Equal("a\tb.txt", entry.Key);
        Assert.Equal((1, 0), entry.Value);
    }

    [Fact]
    public void ParseNumstatZ_PathOfOnlyWhitespace_KeepsFollowingRecords()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes("1\t0\t \0" + "2\t0\tb.txt\0" + "3\t0\tc.txt\0");

        // Act
        var ns = DiffParser.ParseNumstatZ(bytes);

        // Assert
        Assert.Equal(3, ns.Count);
        Assert.Equal((1, 0), ns[" "]);
        Assert.Equal((2, 0), ns["b.txt"]);
        Assert.Equal((3, 0), ns["c.txt"]);
    }

    [Fact]
    public void ParseUnified_ReadsSingleFileWithOneHunk()
    {
        // Arrange
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

        // Act
        var files = DiffParser.ParseUnified(Encoding.UTF8.GetBytes(raw));

        // Assert
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
    public void ParseUnified_DetectsAddedAndDeletedFiles()
    {
        // Arrange
        const string raw =
            "diff --git a/new.txt b/new.txt\n" +
            "new file mode 100644\n" +
            "--- /dev/null\n" +
            "+++ b/new.txt\n" +
            "@@ -0,0 +1,1 @@\n" +
            "+hello\n" +
            "diff --git a/gone.txt b/gone.txt\n" +
            "deleted file mode 100644\n";

        // Act
        var files = DiffParser.ParseUnified(Encoding.UTF8.GetBytes(raw));

        // Assert
        Assert.Equal(2, files.Count);
        Assert.Equal(ChangeType.Added, files[0].ChangeType);
        Assert.Equal(ChangeType.Deleted, files[1].ChangeType);
    }

    [Fact]
    public void ParseUnified_FlagsBinaryFiles()
    {
        // Arrange
        const string raw =
            "diff --git a/img.png b/img.png\n" +
            "Binary files a/img.png and b/img.png differ\n";

        // Act
        var files = DiffParser.ParseUnified(Encoding.UTF8.GetBytes(raw));

        // Assert
        Assert.True(files.Single().IsBinary);
    }

    [Theory]
    [InlineData("diff --git \"a/a\\\"b.txt\" \"b/a\\\"b.txt\"", "a\"b.txt")]
    [InlineData("diff --git \"a/a\\\\b.txt\" \"b/a\\\\b.txt\"", "a\\b.txt")]
    [InlineData("diff --git \"a/a\\tb.txt\" \"b/a\\tb.txt\"", "a\tb.txt")]
    [InlineData("diff --git \"a/a\\001b.txt\" \"b/a\\001b.txt\"", "a\u0001b.txt")]
    [InlineData("diff --git a/x b/c.txt b/x b/c.txt", "x b/c.txt")]
    public void ParseUnified_HeaderWithQuotedOrSpaceBPath_ReadsPath(string header, string expectedPath)
    {
        // Act
        var files = DiffParser.ParseUnified(Encoding.UTF8.GetBytes(header + "\nold mode 100644\nnew mode 100755\n"));

        // Assert
        var file = Assert.Single(files);
        Assert.Equal(expectedPath, file.Path);
        Assert.Equal(expectedPath, file.OldPath);
    }

    [Theory]
    [InlineData("diff --git \"a/caf\\303\\251.txt\" \"b/caf\\303\\251.txt\"")]
    [InlineData("diff --git \"a/x.txt")]
    [InlineData("diff --git \"a/x.txt\\")]
    [InlineData("diff --git \"a/x.txt\"\"b/x.txt\"")]
    [InlineData("diff --git a/x\"y")]
    [InlineData("diff --git a/x \"b/x\"y")]
    [InlineData("diff --git a/x.txt")]
    public void ParseUnified_UnreadableHeader_KeepsHeaderLineAsPath(string header)
    {
        // Act
        var files = DiffParser.ParseUnified(Encoding.UTF8.GetBytes(header + "\n"));

        // Assert
        var file = Assert.Single(files);
        Assert.Equal(header, file.Path);
        Assert.Null(file.OldPath);
    }

    [Theory]
    [MemberData(nameof(TextOutsideFileSections))]
    public void ParseUnified_TextOutsideDiffGitSection_ReachesNoFile(string raw)
    {
        // Act
        var files = DiffParser.ParseUnified(Encoding.UTF8.GetBytes(raw));

        // Assert
        var file = Assert.Single(files);
        Assert.Equal(RealFile, file.Path);
        Assert.Equal(ChangeType.Modified, file.ChangeType);
        Assert.Equal(1, file.Additions);
        Assert.Equal(1, file.Deletions);
        var hunk = Assert.Single(file.Hunks);
        Assert.Equal(RealHunkHeader, hunk.Header);
    }

    [Fact]
    public void AssembleDiffResult_EnrichesFromNumstat()
    {
        // Arrange
        var input = new List<FileDiff>
        {
            new() { Path = "foo.txt", Additions = 0, Deletions = 0 },
        };
        var numstat = new Dictionary<string, (int Additions, int Deletions)>
        {
            ["foo.txt"] = (5, 2),
        };

        // Act
        var result = DiffParser.AssembleDiffResult(input, WorktreeMode, numstat: numstat);

        // Assert
        Assert.Equal(5, result.Files[0].Additions);
        Assert.Equal(2, result.Files[0].Deletions);
        Assert.Equal(5, result.TotalAdditions);
        Assert.Equal(2, result.TotalDeletions);
        Assert.Equal(WorktreeMode, result.Mode);
    }

    [Fact]
    public void AssembleDiffResult_WithoutNumstatSumsProvidedCounts()
    {
        // Arrange
        var input = new List<FileDiff>
        {
            new() { Path = "a.cs", Additions = 2, Deletions = 1 },
            new() { Path = "b.cs", Additions = 3, Deletions = 4 },
        };

        // Act
        var result = DiffParser.AssembleDiffResult(input, WorktreeMode);

        // Assert
        Assert.Equal(2, result.Files.Count);
        Assert.Equal(5, result.TotalAdditions);
        Assert.Equal(5, result.TotalDeletions);
        Assert.Equal(WorktreeMode, result.Mode);
        Assert.False(result.Truncated);
    }

    [Theory]
    [MemberData(nameof(RenameAndCopyDiffs))]
    public void ParseUnified_SetsChangeTypeAndOldPathForRenamesAndCopies(
        string raw, ChangeType expectedChange, string expectedOldPath, string expectedPath)
    {
        // Act
        var files = DiffParser.ParseUnified(Encoding.UTF8.GetBytes(raw));

        // Assert
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
    public void ChangeType_SerializesAsLowercaseString(ChangeType value, string expected)
    {
        // Act
        var json = System.Text.Json.JsonSerializer.Serialize(new FileDiff { Path = "x", ChangeType = value });

        // Assert
        Assert.Contains($"\"change_type\":\"{expected}\"", json);
    }

    [Fact]
    public void ChangeType_RoundTripsThroughJson()
    {
        // Arrange
        var original = new FileDiff { Path = "x", ChangeType = ChangeType.Renamed };

        // Act
        var json = System.Text.Json.JsonSerializer.Serialize(original);

        var restored = System.Text.Json.JsonSerializer.Deserialize<FileDiff>(json);

        // Assert
        Assert.Equal(ChangeType.Renamed, restored.ChangeType);
    }

    [Theory]
    [MemberData(nameof(PlainNameStatusEntries))]
    public void ParseNameStatusZ_MapsPlainStatusLetters(string raw, ChangeType expected)
    {
        // Act
        var result = DiffParser.ParseNameStatusZ(Encoding.UTF8.GetBytes(raw));

        // Assert
        Assert.Equal(expected, result[PlainStatusPath]);
    }

    [Fact]
    public void ParseNameStatusZ_KeysRenameOnNewPath()
    {
        // Arrange
        const string oldPath = "old/path.cs";
        const string newPath = "new/path.cs";
        var bytes = Encoding.UTF8.GetBytes($"R100\0{oldPath}\0{newPath}\0");

        // Act
        var result = DiffParser.ParseNameStatusZ(bytes);

        // Assert
        Assert.Equal(ChangeType.Renamed, result[newPath]);
        Assert.False(result.ContainsKey(oldPath));
    }

    [Fact]
    public void ParseNameStatusZ_KeysCopyOnNewPath()
    {
        // Arrange
        const string origPath = "orig.cs";
        const string copyPath = "copy.cs";
        var bytes = Encoding.UTF8.GetBytes($"C075\0{origPath}\0{copyPath}\0");

        // Act
        var result = DiffParser.ParseNameStatusZ(bytes);

        // Assert
        Assert.Equal(ChangeType.Copied, result[copyPath]);
        Assert.False(result.ContainsKey(origPath));
    }

    [Fact]
    public void ParseNameStatusZ_ReturnsEmptyForEmptyInput()
    {
        // Act
        var result = DiffParser.ParseNameStatusZ(Array.Empty<byte>());

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ParseNameStatusZ_IgnoresTrailingEmptyToken()
    {
        // Act
        var result = DiffParser.ParseNameStatusZ(Encoding.UTF8.GetBytes($"M\0{PlainStatusPath}\0"));

        // Assert
        Assert.Equal(ChangeType.Modified, Assert.Single(result).Value);
    }

    [Fact]
    public void AssembleDiffResult_OverlaysChangeTypeWhenUnknown()
    {
        // Arrange
        var input = new List<FileDiff>
        {
            new() { Path = "foo.cs", ChangeType = ChangeType.Unknown },
        };
        var changeTypes = new Dictionary<string, ChangeType>
        {
            ["foo.cs"] = ChangeType.Added,
        };

        // Act
        var result = DiffParser.AssembleDiffResult(input, WorktreeMode, changeTypes: changeTypes);

        // Assert
        Assert.Equal(ChangeType.Added, result.Files[0].ChangeType);
    }

    [Fact]
    public void AssembleDiffResult_DoesNotClobberExistingChangeType()
    {
        // Arrange
        var input = new List<FileDiff>
        {
            new() { Path = "foo.cs", ChangeType = ChangeType.Modified },
        };
        var changeTypes = new Dictionary<string, ChangeType>
        {
            ["foo.cs"] = ChangeType.Added,
        };

        // Act
        var result = DiffParser.AssembleDiffResult(input, WorktreeMode, changeTypes: changeTypes);

        // Assert
        Assert.Equal(ChangeType.Modified, result.Files[0].ChangeType);
    }

    [Theory]
    [MemberData(nameof(PatchAndNumstatPairs))]
    public void HasPathMissingFromNumstat_ParsedPatchAndNumstat_ReturnsExpected(string patch, string numstat, bool expected)
    {
        // Arrange
        var files = DiffParser.ParseUnified(Encoding.UTF8.GetBytes(patch));
        var counts = DiffParser.ParseNumstatZ(Encoding.UTF8.GetBytes(numstat));

        // Act
        var missing = DiffParser.HasPathMissingFromNumstat(files, counts);

        // Assert
        Assert.Equal(expected, missing);
    }

    [Theory]
    [MemberData(nameof(UnmergedPathPatches))]
    public void HasUnreadableUnmergedPath_ReportsRawLineBreakNameOnUnmergedLine(string patch, string numstatPath, bool expected)
    {
        // Arrange
        var numstat = new Dictionary<string, (int Additions, int Deletions)> { [numstatPath] = (0, 0) };

        // Act
        var result = DiffParser.HasUnreadableUnmergedPath(Encoding.UTF8.GetBytes(patch), numstat);

        // Assert
        Assert.Equal(expected, result);
    }

    #region Test Helpers

    public static TheoryData<string> TextOutsideFileSections()
    {
        return new()
        {
            "diff --cc m.txt\n" + CombinedSectionBody + RealFileSection,
            RealFileSection +
            "diff --cc gone.txt\n" +
            "deleted file mode 100644,100644\n" +
            "@@@ -1,1 -1,1 +0,0 @@@\n" +
            "--gone\n",
            "diff --combined m.txt\n" + CombinedSectionBody + RealFileSection,
            "@@ -5 +5 @@\n" + "+fake\n" + RealFileSection,
            "rename to fake.txt\n" + RealFileSection,
            "copy to fake.txt\n" + RealFileSection,
        };
    }

    public static TheoryData<string, string, bool> UnmergedPathPatches()
    {
        return new()
        {
            { UnmergedLinePrefix + ForgedUnmergedPath + "\n", ForgedUnmergedPath, true },
            { UnmergedLinePrefix + "m.txt\n", "m.txt", false },
            { RealFileSection, "real\nname.txt", false },
        };
    }

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
            {
                "diff --git \"a/old\\\"q.txt\" b/new.txt\n" +
                "similarity index 95%\n" +
                "rename from \"old\\\"q.txt\"\n" +
                "rename to new.txt\n",
                ChangeType.Renamed,
                "old\"q.txt",
                "new.txt"
            },
            {
                "diff --git a/old.txt \"b/new\\\"q.txt\"\n" +
                "similarity index 95%\n" +
                "rename from old.txt\n" +
                "rename to \"new\\\"q.txt\"\n",
                ChangeType.Renamed,
                "old.txt",
                "new\"q.txt"
            },
            {
                "diff --git a/x b/old.txt b/new.txt\n" +
                "similarity index 95%\n" +
                "rename from x b/old.txt\n" +
                "rename to new.txt\n",
                ChangeType.Renamed,
                "x b/old.txt",
                "new.txt"
            },
            {
                "diff --git a/x b/orig.txt b/copy.txt\n" +
                "similarity index 100%\n" +
                "copy from x b/orig.txt\n" +
                "copy to copy.txt\n",
                ChangeType.Copied,
                "x b/orig.txt",
                "copy.txt"
            },
        };
    }

    public static TheoryData<string, string, bool> PatchAndNumstatPairs()
    {
        return new()
        {
            {
                "diff --git a/old.txt \"b/new\\\"q.txt\"\n" +
                "similarity index 90%\n" +
                "rename from old.txt\n" +
                "rename to \"new\\\"q.txt\"\n",
                "1\t1\t\0old.txt\0new\"q.txt\0",
                false
            },
            {
                "diff --git a/x.bin b/x.bin\n" +
                "Binary files a/x.bin and b/x.bin differ\n",
                "-\t-\tx.bin\0",
                false
            },
            {
                "diff --git a/x b/s.sh b/x b/s.sh\n" +
                "old mode 100644\n" +
                "new mode 100755\n",
                "0\t0\tx b/s.sh\0",
                false
            },
            {
                "diff --git a/  b/ \n" +
                "old mode 100644\n" +
                "new mode 100755\n",
                "0\t0\t \0",
                false
            },
            {
                "diff --git \"a/caf\\303\\251.txt\" \"b/caf\\303\\251.txt\"\n",
                "1\t0\tcafé.txt\0",
                true
            },
        };
    }

    #endregion
}