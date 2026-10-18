using System.Text;
using RaccoonNinja.McpToolset.Server.GitOps.Parsers;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Parsers;

public sealed class LsTreeParserTests
{
    private const string ObjectId = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public void Parse_BlobWithPaddedSize_ReadsEveryField()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes($"100644 blob {ObjectId}      24\tREADME.md\0");

        // Act
        var result = LsTreeParser.Parse(bytes);

        // Assert
        var entry = Assert.Single(result);
        Assert.Equal("100644", entry.Mode);
        Assert.Equal("blob", entry.ObjectType);
        Assert.Equal(ObjectId, entry.ObjectId);
        Assert.Equal(24, entry.Size);
        Assert.Equal("README.md", entry.Path);
    }

    [Fact]
    public void Parse_TreeEntry_HasNullSize()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes($"040000 tree {ObjectId}       -\tsrc\0");

        // Act
        var result = LsTreeParser.Parse(bytes);

        // Assert
        var entry = Assert.Single(result);
        Assert.Equal("tree", entry.ObjectType);
        Assert.Null(entry.Size);
    }

    [Fact]
    public void Parse_NestedPath_IsKeptVerbatim()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes($"100644 blob {ObjectId}       7\ta/b/c.txt\0");

        // Act
        var result = LsTreeParser.Parse(bytes);

        // Assert
        Assert.Equal("a/b/c.txt", Assert.Single(result).Path);
    }

    [Fact]
    public void Parse_BadSizeToken_IsNull()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes($"100644 blob {ObjectId}     BAD\tbroken.txt\0");

        // Act
        var result = LsTreeParser.Parse(bytes);

        // Assert
        var entry = Assert.Single(result);
        Assert.Null(entry.Size);
        Assert.Equal("broken.txt", entry.Path);
    }

    [Fact]
    public void Parse_MalformedRecord_IsSkipped()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes($"junk\0100644 blob {ObjectId}       3\tok.txt\0");

        // Act
        var result = LsTreeParser.Parse(bytes);

        // Assert
        Assert.Equal("ok.txt", Assert.Single(result).Path);
    }
}