using System.Text.Json;

namespace RaccoonNinja.McpToolset.Common.Mcp.Tests;

public sealed class ArgumentShapeValidatorTests
{
    private const string ToolSchema = """
        {
          "type": "object",
          "properties": {
            "pattern": {},
            "is_regex": {},
            "glob": {},
            "regex": {},
            "paths": {},
            "cwd": {}
          },
          "required": ["pattern"]
        }
        """;

    private const string NoRequiredSchema = """
        {
          "type": "object",
          "properties": {
            "days_back": {},
            "limit": {}
          }
        }
        """;

    [Fact]
    public void Validate_MissingRequired_IsInvalidAndNamesIt()
    {
        // Arrange
        var schema = Parse(ToolSchema);

        // Act
        var result = ArgumentShapeValidator.Validate(schema, ["glob"]);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(["pattern"], result.MissingRequired);
        Assert.Empty(result.Unknown);
    }

    [Fact]
    public void Validate_CamelCaseUnknown_SuggestsSnakeCaseTwin()
    {
        // Arrange
        var schema = Parse(ToolSchema);

        // Act
        var result = ArgumentShapeValidator.Validate(schema, ["pattern", "isRegex"]);

        // Assert
        Assert.False(result.IsValid);
        var unknown = Assert.Single(result.Unknown);
        Assert.Equal("isRegex", unknown.Given);
        Assert.Equal("is_regex", unknown.DidYouMean);
        Assert.Empty(result.MissingRequired);
    }

    [Fact]
    public void Validate_PascalCaseName_IsUnknownAndSuggestsLowercaseTwin()
    {
        // Arrange
        var schema = Parse(ToolSchema);

        // Act
        var result = ArgumentShapeValidator.Validate(schema, ["Pattern"]);

        // Assert
        var unknown = Assert.Single(result.Unknown);
        Assert.Equal("Pattern", unknown.Given);
        Assert.Equal("pattern", unknown.DidYouMean);
        Assert.Contains("pattern", result.MissingRequired);
    }

    [Fact]
    public void Validate_PluralUnknown_SuggestsSingular()
    {
        // Arrange
        var schema = Parse(ToolSchema);

        // Act
        var result = ArgumentShapeValidator.Validate(schema, ["pattern", "globs"]);

        // Assert
        var unknown = Assert.Single(result.Unknown);
        Assert.Equal("globs", unknown.Given);
        Assert.Equal("glob", unknown.DidYouMean);
    }

    [Fact]
    public void Validate_UnknownWithNoCloseMatch_HasNoSuggestion()
    {
        // Arrange
        var schema = Parse(ToolSchema);

        // Act
        var result = ArgumentShapeValidator.Validate(schema, ["pattern", "zzzzzzzz"]);

        // Assert
        var unknown = Assert.Single(result.Unknown);
        Assert.Equal("zzzzzzzz", unknown.Given);
        Assert.Null(unknown.DidYouMean);
    }

    [Fact]
    public void Validate_WellFormedCall_IsValidAndListsExpectedInSchemaOrder()
    {
        // Arrange
        var schema = Parse(ToolSchema);

        // Act
        var result = ArgumentShapeValidator.Validate(schema, ["pattern", "is_regex", "glob"]);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Unknown);
        Assert.Empty(result.MissingRequired);
        Assert.Equal(["pattern", "is_regex", "glob", "regex", "paths", "cwd"], result.Expected);
    }

    [Fact]
    public void Validate_EmptyArgumentsWithRequired_IsInvalid()
    {
        // Arrange
        var schema = Parse(ToolSchema);

        // Act
        var result = ArgumentShapeValidator.Validate(schema, []);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(["pattern"], result.MissingRequired);
        Assert.Empty(result.Unknown);
    }

    [Fact]
    public void Validate_NoRequiredArrayAndEmptyArguments_IsValid()
    {
        // Arrange
        var schema = Parse(NoRequiredSchema);

        // Act
        var result = ArgumentShapeValidator.Validate(schema, []);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.MissingRequired);
    }

    [Fact]
    public void Validate_MoreThanFiveUnknown_EchoesFiveButStaysInvalid()
    {
        // Arrange
        var schema = Parse(ToolSchema);
        string[] arguments = ["pattern", "u1", "u2", "u3", "u4", "u5", "u6", "u7"];

        // Act
        var result = ArgumentShapeValidator.Validate(schema, arguments);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(5, result.Unknown.Count);
    }

    [Fact]
    public void Validate_MultiByteKeyBeyondCap_TruncatesOnRuneBoundary()
    {
        // Arrange
        var schema = Parse(ToolSchema);
        var emoji = char.ConvertFromUtf32(0x1F600);
        var longKey = string.Concat(Enumerable.Repeat(emoji, 40));

        // Act
        var result = ArgumentShapeValidator.Validate(schema, ["pattern", longKey]);

        // Assert
        var unknown = Assert.Single(result.Unknown);
        Assert.Equal(64, unknown.Given.Length);
        Assert.Equal(32, unknown.Given.EnumerateRunes().Count());
        Assert.False(char.IsHighSurrogate(unknown.Given[^1]));
    }

    [Fact]
    public void Validate_KeyWithControlCharacters_StripsThem()
    {
        // Arrange
        var schema = Parse(ToolSchema);
        var noisyKey = "gl" + (char)0 + "ob" + (char)9 + "s";

        // Act
        var result = ArgumentShapeValidator.Validate(schema, ["pattern", noisyKey]);

        // Assert
        var unknown = Assert.Single(result.Unknown);
        Assert.Equal("globs", unknown.Given);
        Assert.Equal("glob", unknown.DidYouMean);
    }

    private static JsonElement Parse(string json)
        => JsonSerializer.Deserialize<JsonElement>(json);
}