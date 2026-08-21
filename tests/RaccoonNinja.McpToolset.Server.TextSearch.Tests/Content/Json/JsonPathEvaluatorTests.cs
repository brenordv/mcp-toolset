using System.Text.Json.Nodes;
using RaccoonNinja.McpToolset.Server.TextSearch.Content.Json;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tests.Content.Json;

public sealed class JsonPathEvaluatorTests
{
    [Fact]
    public void Evaluate_NestedProperty_Hits()
    {
        // Arrange
        var root = JsonNode.Parse("""{"a":{"b":{"c":42}}}""");
        var segments = JsonPathParser.Parse("a.b.c");

        // Act
        var evaluation = JsonPathEvaluator.Evaluate(root, segments);

        // Assert
        Assert.True(evaluation.Found);
        Assert.Equal(42, evaluation.Value.GetValue<int>());
    }

    [Theory]
    [InlineData(0, "first")]
    [InlineData(2, "third")]
    [InlineData(-1, "third")]
    [InlineData(-3, "first")]
    public void Evaluate_Index_FollowsPythonSemantics(int index, string expected)
    {
        // Arrange
        var root = JsonNode.Parse("""["first","second","third"]""");
        var segments = new[] { JsonPathSegment.ForIndex(index) };

        // Act
        var evaluation = JsonPathEvaluator.Evaluate(root, segments);

        // Assert
        Assert.True(evaluation.Found);
        Assert.Equal(expected, evaluation.Value.GetValue<string>());
    }

    [Fact]
    public void Evaluate_ExplicitNullValue_HitsWithNull()
    {
        // Arrange
        var root = JsonNode.Parse("""{"a":null}""");
        var segments = JsonPathParser.Parse("a");

        // Act
        var evaluation = JsonPathEvaluator.Evaluate(root, segments);

        // Assert
        Assert.True(evaluation.Found);
        Assert.Null(evaluation.Value);
    }

    [Fact]
    public void Evaluate_MissingKey_MissesAtThatSegment()
    {
        // Arrange
        var root = JsonNode.Parse("""{"a":{"b":1}}""");
        var segments = JsonPathParser.Parse("a.missing.x");

        // Act
        var evaluation = JsonPathEvaluator.Evaluate(root, segments);

        // Assert
        Assert.False(evaluation.Found);
        Assert.Equal(1, evaluation.FailedIndex);
        Assert.IsType<JsonObject>(evaluation.FailedNode);
    }

    [Fact]
    public void Evaluate_PropertyLookup_IsCaseSensitive()
    {
        // Arrange
        var root = JsonNode.Parse("""{"foo":1}""");
        var segments = JsonPathParser.Parse("Foo");

        // Act
        var evaluation = JsonPathEvaluator.Evaluate(root, segments);

        // Assert
        Assert.False(evaluation.Found);
        Assert.Equal(0, evaluation.FailedIndex);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(-4)]
    public void Evaluate_IndexOutOfRange_Misses(int index)
    {
        // Arrange
        var root = JsonNode.Parse("""[1,2,3]""");
        var segments = new[] { JsonPathSegment.ForIndex(index) };

        // Act
        var evaluation = JsonPathEvaluator.Evaluate(root, segments);

        // Assert
        Assert.False(evaluation.Found);
        Assert.IsType<JsonArray>(evaluation.FailedNode);
    }

    [Fact]
    public void Evaluate_IndexIntoScalar_Misses()
    {
        // Arrange
        var root = JsonNode.Parse("""{"a":"text"}""");
        var segments = JsonPathParser.Parse("a[0]");

        // Act
        var evaluation = JsonPathEvaluator.Evaluate(root, segments);

        // Assert
        Assert.False(evaluation.Found);
        Assert.Equal(1, evaluation.FailedIndex);
    }

    [Fact]
    public void Evaluate_PropertyOnArray_Misses()
    {
        // Arrange
        var root = JsonNode.Parse("""[1,2]""");
        var segments = JsonPathParser.Parse("name");

        // Act
        var evaluation = JsonPathEvaluator.Evaluate(root, segments);

        // Assert
        Assert.False(evaluation.Found);
        Assert.Equal(0, evaluation.FailedIndex);
    }

    [Fact]
    public void Evaluate_NullRootWithSegments_MissesAtRoot()
    {
        // Arrange
        var segments = JsonPathParser.Parse("a");

        // Act
        var evaluation = JsonPathEvaluator.Evaluate(null, segments);

        // Assert
        Assert.False(evaluation.Found);
        Assert.Equal(0, evaluation.FailedIndex);
        Assert.Null(evaluation.FailedNode);
    }

    [Fact]
    public void Evaluate_NoSegments_ReturnsTheRoot()
    {
        // Arrange
        var root = JsonNode.Parse("""{"a":1}""");

        // Act
        var evaluation = JsonPathEvaluator.Evaluate(root, []);

        // Assert
        Assert.True(evaluation.Found);
        Assert.Same(root, evaluation.Value);
    }
}