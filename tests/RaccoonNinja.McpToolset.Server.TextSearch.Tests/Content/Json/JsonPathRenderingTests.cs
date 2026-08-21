using RaccoonNinja.McpToolset.Server.TextSearch.Content.Json;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tests.Content.Json;

public sealed class JsonPathRenderingTests
{
    [Fact]
    public void Render_ZeroSegments_IsTheRootMarker()
    {
        // Act
        var text = JsonPathRendering.Render([], 0);

        // Assert
        Assert.Equal("$", text);
    }

    [Fact]
    public void Render_NamesAndIndexes_RoundTripTheDotForm()
    {
        // Arrange
        var segments = JsonPathParser.Parse("a.b[2][-1].c");

        // Act
        var text = JsonPathRendering.Render(segments, segments.Count);

        // Assert
        Assert.Equal("a.b[2][-1].c", text);
    }

    [Fact]
    public void Render_PrefixCount_StopsEarly()
    {
        // Arrange
        var segments = JsonPathParser.Parse("a.b.c");

        // Act
        var text = JsonPathRendering.Render(segments, 2);

        // Assert
        Assert.Equal("a.b", text);
    }

    [Theory]
    [InlineData("deps[\"lodash.merge\"]", "deps[\"lodash.merge\"]")]
    [InlineData("deps['single.quoted']", "deps[\"single.quoted\"]")]
    [InlineData("a[\"with \\\"quote\\\"\"]", "a[\"with \\\"quote\\\"\"]")]
    [InlineData("a[\"\"]", "a[\"\"]")]
    [InlineData("[\"$\"].x", "[\"$\"].x")]
    public void Render_NamesTheDotFormCannotSpell_ComeBackQuoted(string jsonPath, string expected)
    {
        // Arrange
        var segments = JsonPathParser.Parse(jsonPath);

        // Act
        var text = JsonPathRendering.Render(segments, segments.Count);

        // Assert
        Assert.Equal(expected, text);
    }
}