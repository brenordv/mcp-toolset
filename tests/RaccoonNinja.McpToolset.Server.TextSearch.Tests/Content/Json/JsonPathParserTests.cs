using RaccoonNinja.McpToolset.Server.TextSearch.Content.Json;
using RaccoonNinja.McpToolset.Server.TextSearch.Errors;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tests.Content.Json;

public sealed class JsonPathParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_Blank_MeansTheDocumentRoot(string jsonPath)
    {
        // Act
        var segments = JsonPathParser.Parse(jsonPath);

        // Assert
        Assert.Empty(segments);
    }

    [Fact]
    public void Parse_DottedNames_YieldsPropertySegments()
    {
        // Act
        var segments = JsonPathParser.Parse("compilerOptions.target");

        // Assert
        Assert.Equal(2, segments.Count);
        Assert.Equal("compilerOptions", segments[0].Property);
        Assert.Equal("target", segments[1].Property);
        Assert.False(segments[0].IsIndex);
    }

    [Fact]
    public void Parse_Indexes_ComposeWithNamesAndNegatives()
    {
        // Act
        var segments = JsonPathParser.Parse("a.b[2][-1].c");

        // Assert
        Assert.Equal(5, segments.Count);
        Assert.Equal("a", segments[0].Property);
        Assert.Equal("b", segments[1].Property);
        Assert.Equal(2, segments[2].Index);
        Assert.Equal(-1, segments[3].Index);
        Assert.True(segments[3].IsIndex);
        Assert.Equal("c", segments[4].Property);
    }

    [Fact]
    public void Parse_RootIndex_IsAllowed()
    {
        // Act
        var segments = JsonPathParser.Parse("[0].name");

        // Assert
        Assert.Equal(2, segments.Count);
        Assert.True(segments[0].IsIndex);
        Assert.Equal(0, segments[0].Index);
        Assert.Equal("name", segments[1].Property);
    }

    [Theory]
    [InlineData("deps[\"lodash.merge\"]", "lodash.merge")]
    [InlineData("deps['lodash.merge']", "lodash.merge")]
    [InlineData("deps[\"with \\\"quote\\\"\"]", "with \"quote\"")]
    [InlineData("deps[\"back\\\\slash\"]", "back\\slash")]
    [InlineData("deps[\"\"]", "")]
    public void Parse_QuotedNames_UnescapeIntoPropertySegments(string jsonPath, string expected)
    {
        // Act
        var segments = JsonPathParser.Parse(jsonPath);

        // Assert
        Assert.Equal(2, segments.Count);
        Assert.Equal(expected, segments[1].Property);
        Assert.False(segments[1].IsIndex);
    }

    [Fact]
    public void Parse_RootDollarAlone_MeansTheDocumentRoot()
    {
        // Act
        var segments = JsonPathParser.Parse("$");

        // Assert
        Assert.Empty(segments);
    }

    [Fact]
    public void Parse_RootDollarWithDot_IsIgnored()
    {
        // Act
        var segments = JsonPathParser.Parse("$.a");

        // Assert
        Assert.Equal("a", Assert.Single(segments).Property);
    }

    [Fact]
    public void Parse_RootDollarWithBracket_IsIgnored()
    {
        // Act
        var segments = JsonPathParser.Parse("$[0]");

        // Assert
        Assert.Equal(0, Assert.Single(segments).Index);
    }

    [Fact]
    public void Parse_DollarFollowedByNameCharacter_IsALiteralName()
    {
        // Act
        var segments = JsonPathParser.Parse("$schema");

        // Assert
        Assert.Equal("$schema", Assert.Single(segments).Property);
    }

    [Theory]
    [InlineData(".a")]
    [InlineData("a.")]
    [InlineData("a..b")]
    [InlineData("a]b")]
    [InlineData("a[")]
    [InlineData("a[1")]
    [InlineData("a[]")]
    [InlineData("a[-]")]
    [InlineData("a[1.5]")]
    [InlineData("a['x]")]
    [InlineData("a[\"x\\q\"]")]
    [InlineData("a[\"x\"b]")]
    [InlineData("a[99999999999999999999]")]
    public void Parse_MalformedPath_ReportsInvalidArgument(string jsonPath)
    {
        // Act
        var exception = Assert.Throws<TextSearchException>(() => JsonPathParser.Parse(jsonPath));

        // Assert
        Assert.Equal(ErrorCodes.InvalidArgument, exception.Code);
    }

    [Fact]
    public void Parse_OverLengthCap_ReportsInvalidArgument()
    {
        // Arrange
        var jsonPath = new string('a', JsonPathParser.MaxLength + 1);

        // Act
        var exception = Assert.Throws<TextSearchException>(() => JsonPathParser.Parse(jsonPath));

        // Assert
        Assert.Equal(ErrorCodes.InvalidArgument, exception.Code);
    }

    [Fact]
    public void Parse_OverSegmentCap_ReportsInvalidArgument()
    {
        // Arrange
        var jsonPath = string.Join('.', Enumerable.Repeat("a", JsonPathParser.MaxSegments + 1));

        // Act
        var exception = Assert.Throws<TextSearchException>(() => JsonPathParser.Parse(jsonPath));

        // Assert
        Assert.Equal(ErrorCodes.InvalidArgument, exception.Code);
    }
}