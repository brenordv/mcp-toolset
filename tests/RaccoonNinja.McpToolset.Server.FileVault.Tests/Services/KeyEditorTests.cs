using System.Text.Json;
using RaccoonNinja.McpToolset.Server.FileVault.Errors;
using RaccoonNinja.McpToolset.Server.FileVault.Services;
using YamlDotNet.RepresentationModel;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tests.Services;

/// <summary>Unit tests for <see cref="KeyEditor"/> dotted key-path editing of JSON and YAML documents.</summary>
public class KeyEditorTests
{
    [Fact]
    public void SetJsonKey_TopLevelKey_ReplacesValueAndPreservesInsertionOrder()
    {
        // Arrange
        const string source = "{\"b\":1,\"a\":2}";

        // Act
        var result = KeyEditor.SetJsonKey(source, "b", Element("42"));

        // Assert
        Assert.Equal(JsonText(
            "{",
            "  \"b\": 42,",
            "  \"a\": 2",
            "}"), result);
    }

    [Fact]
    public void SetJsonKey_NestedObjectPath_SetsDeepValue()
    {
        // Arrange
        const string source = "{\"outer\":{\"inner\":{\"value\":1}},\"tail\":true}";

        // Act
        var result = KeyEditor.SetJsonKey(source, "outer.inner.value", Element("\"done\""));

        // Assert
        Assert.Equal(JsonText(
            "{",
            "  \"outer\": {",
            "    \"inner\": {",
            "      \"value\": \"done\"",
            "    }",
            "  },",
            "  \"tail\": true",
            "}"), result);
    }

    [Fact]
    public void SetJsonKey_ArrayIndexPath_SetsElementInPlace()
    {
        // Arrange
        const string source = "{\"items\":[{\"name\":\"old\"},{\"name\":\"other\"}]}";

        // Act
        var result = KeyEditor.SetJsonKey(source, "items.0.name", Element("\"new\""));

        // Assert
        Assert.Equal(JsonText(
            "{",
            "  \"items\": [",
            "    {",
            "      \"name\": \"new\"",
            "    },",
            "    {",
            "      \"name\": \"other\"",
            "    }",
            "  ]",
            "}"), result);
    }

    [Fact]
    public void SetJsonKey_RootArrayIndex_SetsElement()
    {
        // Arrange
        const string source = "[10,20,30]";

        // Act
        var result = KeyEditor.SetJsonKey(source, "1", Element("99"));

        // Assert
        Assert.Equal(JsonText(
            "[",
            "  10,",
            "  99,",
            "  30",
            "]"), result);
    }

    [Fact]
    public void SetJsonKey_NullValue_WritesJsonNull()
    {
        // Arrange
        const string source = "{\"a\":1}";

        // Act
        var result = KeyEditor.SetJsonKey(source, "a", Element("null"));

        // Assert
        Assert.Equal(JsonText(
            "{",
            "  \"a\": null",
            "}"), result);
    }

    [Fact]
    public void SetJsonKey_ObjectValue_SerializedStructurally()
    {
        // Arrange
        const string source = "{\"config\":null}";

        // Act
        var result = KeyEditor.SetJsonKey(source, "config", Element("{\"x\":1,\"y\":[true,null]}"));

        // Assert
        Assert.Equal(JsonText(
            "{",
            "  \"config\": {",
            "    \"x\": 1,",
            "    \"y\": [",
            "      true,",
            "      null",
            "    ]",
            "  }",
            "}"), result);
    }

    [Fact]
    public void SetJsonKey_PathWithEmptySegments_EqualsCompactPath()
    {
        // Arrange
        const string source = "{\"a\":{\"b\":1}}";

        // Act
        var doubleDot = KeyEditor.SetJsonKey(source, "a..b", Element("2"));
        var singleDot = KeyEditor.SetJsonKey(source, "a.b", Element("2"));

        // Assert
        Assert.Equal(singleDot, doubleDot);
        Assert.Equal(JsonText(
            "{",
            "  \"a\": {",
            "    \"b\": 2",
            "  }",
            "}"), doubleDot);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    public void SetJsonKey_AllEmptyPath_ThrowsKeyPathNotFound(string keyPath)
    {
        // Arrange
        const string source = "{\"a\":1}";

        // Act
        var act = () => KeyEditor.SetJsonKey(source, keyPath, Element("2"));

        // Assert
        var exception = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.KeyPathNotFound, exception.Code);
    }

    [Fact]
    public void SetJsonKey_MissingKey_ThrowsKeyPathNotFound()
    {
        // Arrange
        const string source = "{\"a\":1}";

        // Act
        var act = () => KeyEditor.SetJsonKey(source, "missing", Element("2"));

        // Assert
        var exception = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.KeyPathNotFound, exception.Code);
        Assert.Contains("missing", exception.Message);
    }

    [Fact]
    public void SetJsonKey_MissingIntermediateKey_ThrowsInsteadOfCreatingIt()
    {
        // Arrange
        const string source = "{\"a\":{\"b\":1}}";

        // Act
        var act = () => KeyEditor.SetJsonKey(source, "a.x.c", Element("2"));

        // Assert
        var exception = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.KeyPathNotFound, exception.Code);
    }

    [Theory]
    [InlineData("items.5")]
    [InlineData("items.-1")]
    [InlineData("items.first")]
    public void SetJsonKey_BadArrayIndex_ThrowsKeyPathNotFound(string keyPath)
    {
        // Arrange
        const string source = "{\"items\":[1,2]}";

        // Act
        var act = () => KeyEditor.SetJsonKey(source, keyPath, Element("9"));

        // Assert
        var exception = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.KeyPathNotFound, exception.Code);
    }

    [Fact]
    public void SetJsonKey_ScalarIntermediateNode_ThrowsKeyPathNotFound()
    {
        // Arrange
        const string source = "{\"a\":1}";

        // Act
        var act = () => KeyEditor.SetJsonKey(source, "a.b", Element("2"));

        // Assert
        var exception = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.KeyPathNotFound, exception.Code);
    }

    [Fact]
    public void SetJsonKey_MalformedDocument_ThrowsInvalidFormat()
    {
        // Arrange
        const string source = "{ not json";

        // Act
        var act = () => KeyEditor.SetJsonKey(source, "a", Element("1"));

        // Assert
        var exception = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.InvalidFormat, exception.Code);
    }

    [Fact]
    public void SetJsonKey_NonAsciiValuesInDocument_AreEmittedRawNotEscaped()
    {
        // Arrange
        const string source = "{\"title\":\"日本語\",\"note\":\"a<b\",\"count\":1}";

        // Act
        var result = KeyEditor.SetJsonKey(source, "count", Element("2"));

        // Assert
        Assert.Contains("日本語", result);
        Assert.Contains("a<b", result);
        Assert.DoesNotContain("\\u", result);
    }

    [Fact]
    public void SetJsonKey_DocumentWithDuplicateKeys_ThrowsInvalidFormat()
    {
        // Arrange
        const string source = "{\"a\":1,\"a\":2}";

        // Act
        var act = () => KeyEditor.SetJsonKey(source, "a", Element("3"));

        // Assert
        var exception = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.InvalidFormat, exception.Code);
    }

    [Fact]
    public void SetYamlKey_TopLevelScalar_UpdatesValueWithDocumentMarkerAndTrailingNewline()
    {
        // Arrange
        const string source = "name: test\ncount: 1\n";

        // Act
        var result = KeyEditor.SetYamlKey(source, "count", Element("5"));

        // Assert
        Assert.Equal("---\nname: test\ncount: 5\n", result);
    }

    [Fact]
    public void SetYamlKey_NestedMappingPath_SetsDeepValueAndKeepsSiblings()
    {
        // Arrange
        const string source = "server:\n  host: localhost\n  port: 8080\nname: app\n";

        // Act
        var result = KeyEditor.SetYamlKey(source, "server.port", Element("9090"));

        // Assert
        Assert.StartsWith("---\n", result);
        Assert.EndsWith("\n", result);
        var root = (YamlMappingNode)ParseYamlRoot(result);
        var server = (YamlMappingNode)root.Children[new YamlScalarNode("server")];
        Assert.Equal("9090", ((YamlScalarNode)server.Children[new YamlScalarNode("port")]).Value);
        Assert.Equal("localhost", ((YamlScalarNode)server.Children[new YamlScalarNode("host")]).Value);
        Assert.Equal("app", ((YamlScalarNode)root.Children[new YamlScalarNode("name")]).Value);
    }

    [Fact]
    public void SetYamlKey_SequenceIndexPath_SetsElement()
    {
        // Arrange
        const string source = "items:\n  - one\n  - two\n";

        // Act
        var result = KeyEditor.SetYamlKey(source, "items.1", Element("\"TWO\""));

        // Assert
        var root = (YamlMappingNode)ParseYamlRoot(result);
        var items = (YamlSequenceNode)root.Children[new YamlScalarNode("items")];
        Assert.Equal("one", ((YamlScalarNode)items.Children[0]).Value);
        Assert.Equal("TWO", ((YamlScalarNode)items.Children[1]).Value);
    }

    [Fact]
    public void SetYamlKey_MultiDocumentSource_EditsFirstDocumentOnly()
    {
        // Arrange
        const string source = "---\nfirst: 1\n---\nsecond: 2\n";

        // Act
        var result = KeyEditor.SetYamlKey(source, "first", Element("9"));

        // Assert
        var stream = new YamlStream();
        stream.Load(new StringReader(result));
        Assert.Equal(2, stream.Documents.Count);
        var first = (YamlMappingNode)stream.Documents[0].RootNode;
        Assert.Equal("9", ((YamlScalarNode)first.Children[new YamlScalarNode("first")]).Value);
        var second = (YamlMappingNode)stream.Documents[1].RootNode;
        Assert.Equal("2", ((YamlScalarNode)second.Children[new YamlScalarNode("second")]).Value);
    }

    [Fact]
    public void SetYamlKey_KeyOnlyInSecondDocument_ThrowsKeyPathNotFound()
    {
        // Arrange
        const string source = "---\nfirst: 1\n---\nsecond: 2\n";

        // Act
        var act = () => KeyEditor.SetYamlKey(source, "second", Element("9"));

        // Assert
        var exception = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.KeyPathNotFound, exception.Code);
    }

    [Theory]
    [InlineData("null", "a: ~")]
    [InlineData("true", "a: true")]
    [InlineData("false", "a: false")]
    [InlineData("42", "a: 42")]
    [InlineData("3.14", "a: 3.14")]
    [InlineData("\"hello\"", "a: hello")]
    [InlineData("\"123\"", "a: \"123\"")]
    [InlineData("\"true\"", "a: \"true\"")]
    [InlineData("\"\"", "a: \"\"")]
    public void SetYamlKey_ScalarValueConversion_EmitsExpectedScalar(string valueJson, string expectedLine)
    {
        // Arrange
        const string source = "a: 1\n";

        // Act
        var result = KeyEditor.SetYamlKey(source, "a", Element(valueJson));

        // Assert
        Assert.Equal("---\n" + expectedLine + "\n", result);
    }

    [Fact]
    public void SetYamlKey_ArrayValue_ConvertsToSequence()
    {
        // Arrange
        const string source = "a: 1\n";

        // Act
        var result = KeyEditor.SetYamlKey(source, "a", Element("[1,\"two\",true]"));

        // Assert
        var root = (YamlMappingNode)ParseYamlRoot(result);
        var sequence = (YamlSequenceNode)root.Children[new YamlScalarNode("a")];
        Assert.Equal(3, sequence.Children.Count);
        Assert.Equal("1", ((YamlScalarNode)sequence.Children[0]).Value);
        Assert.Equal("two", ((YamlScalarNode)sequence.Children[1]).Value);
        Assert.Equal("true", ((YamlScalarNode)sequence.Children[2]).Value);
    }

    [Fact]
    public void SetYamlKey_ObjectValue_ConvertsToMapping()
    {
        // Arrange
        const string source = "config: old\n";

        // Act
        var result = KeyEditor.SetYamlKey(source, "config", Element("{\"x\":1,\"y\":[\"a\",\"b\"]}"));

        // Assert
        var root = (YamlMappingNode)ParseYamlRoot(result);
        var config = (YamlMappingNode)root.Children[new YamlScalarNode("config")];
        Assert.Equal("1", ((YamlScalarNode)config.Children[new YamlScalarNode("x")]).Value);
        var y = (YamlSequenceNode)config.Children[new YamlScalarNode("y")];
        Assert.Equal("a", ((YamlScalarNode)y.Children[0]).Value);
        Assert.Equal("b", ((YamlScalarNode)y.Children[1]).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    public void SetYamlKey_AllEmptyPath_ThrowsKeyPathNotFound(string keyPath)
    {
        // Arrange
        const string source = "a: 1\n";

        // Act
        var act = () => KeyEditor.SetYamlKey(source, keyPath, Element("2"));

        // Assert
        var exception = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.KeyPathNotFound, exception.Code);
    }

    [Fact]
    public void SetYamlKey_MissingKey_ThrowsKeyPathNotFound()
    {
        // Arrange
        const string source = "a: 1\n";

        // Act
        var act = () => KeyEditor.SetYamlKey(source, "b", Element("2"));

        // Assert
        var exception = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.KeyPathNotFound, exception.Code);
    }

    [Fact]
    public void SetYamlKey_MissingIntermediateKey_ThrowsInsteadOfCreatingIt()
    {
        // Arrange
        const string source = "a:\n  b: 1\n";

        // Act
        var act = () => KeyEditor.SetYamlKey(source, "a.c.d", Element("2"));

        // Assert
        var exception = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.KeyPathNotFound, exception.Code);
    }

    [Theory]
    [InlineData("items.3")]
    [InlineData("items.-1")]
    [InlineData("items.first")]
    public void SetYamlKey_BadSequenceIndex_ThrowsKeyPathNotFound(string keyPath)
    {
        // Arrange
        const string source = "items:\n  - one\n";

        // Act
        var act = () => KeyEditor.SetYamlKey(source, keyPath, Element("9"));

        // Assert
        var exception = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.KeyPathNotFound, exception.Code);
    }

    [Fact]
    public void SetYamlKey_ScalarIntermediateNode_ThrowsKeyPathNotFound()
    {
        // Arrange
        const string source = "a: 1\n";

        // Act
        var act = () => KeyEditor.SetYamlKey(source, "a.b", Element("2"));

        // Assert
        var exception = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.KeyPathNotFound, exception.Code);
    }

    [Fact]
    public void SetYamlKey_EmptyDocument_ThrowsInvalidFormat()
    {
        // Arrange
        var source = string.Empty;

        // Act
        var act = () => KeyEditor.SetYamlKey(source, "a", Element("1"));

        // Assert
        var exception = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.InvalidFormat, exception.Code);
    }

    [Fact]
    public void SetYamlKey_MalformedDocument_ThrowsInvalidFormat()
    {
        // Arrange
        var source = "a: [1, 2\n";

        // Act
        var act = () => KeyEditor.SetYamlKey(source, "a", Element("1"));

        // Assert
        var exception = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.InvalidFormat, exception.Code);
    }

    /// <summary>Build a <see cref="JsonElement"/> from raw JSON text (cloned so the document can be disposed).</summary>
    private static JsonElement Element(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    /// <summary>Join lines with LF, matching the editor's pinned-newline JSON output on every OS.</summary>
    private static string JsonText(params string[] lines)
        => string.Join("\n", lines);

    /// <summary>Re-parse emitted YAML and return the first document's root for semantic assertions.</summary>
    private static YamlNode ParseYamlRoot(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        return stream.Documents[0].RootNode;
    }
}