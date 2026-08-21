using System.Text;
using RaccoonNinja.McpToolset.Server.TextSearch.Errors;
using RaccoonNinja.McpToolset.Server.TextSearch.Models;
using RaccoonNinja.McpToolset.Server.TextSearch.Tests.TestSupport;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tests.Tools;

public sealed class ReadJsonToolTests
{
    [Fact]
    public async Task ReadJson_NoJsonPath_ReturnsTheWholeDocument()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("config.json", """{"name":"demo","tags":["a","b"]}""");

        // Act
        var envelope = await harness.ReadJson.InvokeAsync(path: "config.json");

        // Assert
        Assert.Null(envelope.Error);
        var result = Assert.IsType<JsonValueResult>(Assert.Single(envelope.Results));
        Assert.Equal("config.json", result.Path);
        Assert.Null(result.JsonPath);
        Assert.Equal("object", result.Kind);
        Assert.Equal("demo", result.Value["name"].GetValue<string>());
    }

    [Fact]
    public async Task ReadJson_NestedProperty_ReturnsOnlyThatValue()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("tsconfig.json", """{"compilerOptions":{"target":"ES2022","strict":true}}""");

        // Act
        var envelope = await harness.ReadJson.InvokeAsync(path: "tsconfig.json", json_path: "compilerOptions.target");

        // Assert
        Assert.Null(envelope.Error);
        var result = Assert.IsType<JsonValueResult>(Assert.Single(envelope.Results));
        Assert.Equal("compilerOptions.target", result.JsonPath);
        Assert.Equal("string", result.Kind);
        Assert.Equal("ES2022", result.Value.GetValue<string>());
        Assert.Equal("<provided>", envelope.FiltersApplied["path"]);
        Assert.Equal("<provided>", envelope.FiltersApplied["json_path"]);
    }

    [Theory]
    [InlineData("items[1]", "second")]
    [InlineData("items[-1]", "third")]
    [InlineData("items[-3]", "first")]
    public async Task ReadJson_Indexing_FollowsPythonSemantics(string jsonPath, string expected)
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("data.json", """{"items":["first","second","third"]}""");

        // Act
        var envelope = await harness.ReadJson.InvokeAsync(path: "data.json", json_path: jsonPath);

        // Assert
        Assert.Null(envelope.Error);
        var result = Assert.IsType<JsonValueResult>(Assert.Single(envelope.Results));
        Assert.Equal(expected, result.Value.GetValue<string>());
    }

    [Fact]
    public async Task ReadJson_RootArrayIndex_Resolves()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("list.json", """[{"name":"only"}]""");

        // Act
        var envelope = await harness.ReadJson.InvokeAsync(path: "list.json", json_path: "[0].name");

        // Assert
        Assert.Null(envelope.Error);
        var result = Assert.IsType<JsonValueResult>(Assert.Single(envelope.Results));
        Assert.Equal("only", result.Value.GetValue<string>());
    }

    [Fact]
    public async Task ReadJson_QuotedDottedKey_Resolves()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("package.json", """{"dependencies":{"lodash.merge":"4.6.2"}}""");

        // Act
        var envelope = await harness.ReadJson.InvokeAsync(path: "package.json", json_path: "dependencies[\"lodash.merge\"]");

        // Assert
        Assert.Null(envelope.Error);
        var result = Assert.IsType<JsonValueResult>(Assert.Single(envelope.Results));
        Assert.Equal("4.6.2", result.Value.GetValue<string>());
    }

    [Fact]
    public async Task ReadJson_DollarPrefix_IsTolerated()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("a.json", """{"a":{"b":7}}""");

        // Act
        var envelope = await harness.ReadJson.InvokeAsync(path: "a.json", json_path: "$.a.b");

        // Assert
        Assert.Null(envelope.Error);
        var result = Assert.IsType<JsonValueResult>(Assert.Single(envelope.Results));
        Assert.Equal(7, result.Value.GetValue<int>());
    }

    [Fact]
    public async Task ReadJson_ExplicitNullValue_HasKindNull()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("a.json", """{"a":null}""");

        // Act
        var envelope = await harness.ReadJson.InvokeAsync(path: "a.json", json_path: "a");

        // Assert
        Assert.Null(envelope.Error);
        var result = Assert.IsType<JsonValueResult>(Assert.Single(envelope.Results));
        Assert.Equal("null", result.Kind);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData("42", "number")]
    [InlineData("true", "boolean")]
    [InlineData("false", "boolean")]
    [InlineData("\"text\"", "string")]
    [InlineData("null", "null")]
    public async Task ReadJson_ScalarRootDocument_ReportsItsKind(string content, string expectedKind)
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("scalar.json", content);

        // Act
        var envelope = await harness.ReadJson.InvokeAsync(path: "scalar.json");

        // Assert
        Assert.Null(envelope.Error);
        var result = Assert.IsType<JsonValueResult>(Assert.Single(envelope.Results));
        Assert.Equal(expectedKind, result.Kind);
    }

    [Fact]
    public async Task ReadJson_JsoncCommentsAndTrailingCommas_Parse()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write(
            "settings.json",
            """
            {
              // editor block
              "editor": {
                "fontSize": 14, /* trailing comma below */
              },
            }
            """);

        // Act
        var envelope = await harness.ReadJson.InvokeAsync(path: "settings.json", json_path: "editor.fontSize");

        // Assert
        Assert.Null(envelope.Error);
        var result = Assert.IsType<JsonValueResult>(Assert.Single(envelope.Results));
        Assert.Equal(14, result.Value.GetValue<int>());
    }

    [Fact]
    public async Task ReadJson_DuplicateKeys_ReportJsonInvalid()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("dup.json", """{"a":1,"a":2}""");

        // Act
        var envelope = await harness.ReadJson.InvokeAsync(path: "dup.json", json_path: "a");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.JsonInvalid, envelope.Error.Code);
    }

    [Fact]
    public async Task ReadJson_Utf16WithBom_ParsesThroughTheDetector()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        var encoding = Encoding.Unicode;
        harness.WriteBytes("wide.json", [.. encoding.GetPreamble(), .. encoding.GetBytes("""{"a":{"b":"wide"}}""")]);

        // Act
        var envelope = await harness.ReadJson.InvokeAsync(path: "wide.json", json_path: "a.b");

        // Assert
        Assert.Null(envelope.Error);
        var result = Assert.IsType<JsonValueResult>(Assert.Single(envelope.Results));
        Assert.Equal("wide", result.Value.GetValue<string>());
    }

    [Fact]
    public async Task ReadJson_InvalidJson_ReportsJsonInvalidWithOneBasedLine()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("broken.json", "{\n  \"a\": oops\n}");

        // Act
        var envelope = await harness.ReadJson.InvokeAsync(path: "broken.json");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.JsonInvalid, envelope.Error.Code);
        Assert.Equal("file is not valid JSON", envelope.Error.Message);
        Assert.Equal(2L, envelope.Error.Detail["line"]);
    }

    [Fact]
    public async Task ReadJson_MalformedJsonPath_ReportsInvalidArgumentBeforeReadingTheFile()
    {
        // Arrange
        using var harness = new TextSearchHarness();

        // Act
        var envelope = await harness.ReadJson.InvokeAsync(path: "missing.json", json_path: "a..b");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.InvalidArgument, envelope.Error.Code);
    }

    [Fact]
    public async Task ReadJson_MissingKey_ReportsJsonPathNotFoundWithPrefixAndHint()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("a.json", """{"a":{"b":1,"c":2}}""");

        // Act
        var envelope = await harness.ReadJson.InvokeAsync(path: "a.json", json_path: "a.missing.x");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.JsonPathNotFound, envelope.Error.Code);
        Assert.Equal("json_path does not resolve in this document", envelope.Error.Message);
        Assert.Equal("a", envelope.Error.Detail["resolved_prefix"]);
        Assert.Equal("object", envelope.Error.Detail["kind"]);
        Assert.Equal(2, envelope.Error.Detail["property_count"]);
        Assert.Equal(["b", "c"], (string[])envelope.Error.Detail["properties"]);
    }

    [Fact]
    public async Task ReadJson_IndexOutOfRange_ReportsJsonPathNotFoundWithLength()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("a.json", """{"items":[1,2,3]}""");

        // Act
        var envelope = await harness.ReadJson.InvokeAsync(path: "a.json", json_path: "items[-7]");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.JsonPathNotFound, envelope.Error.Code);
        Assert.Equal("items", envelope.Error.Detail["resolved_prefix"]);
        Assert.Equal("array", envelope.Error.Detail["kind"]);
        Assert.Equal(3, envelope.Error.Detail["length"]);
    }

    [Fact]
    public async Task ReadJson_IndexIntoScalar_ReportsJsonPathNotFound()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("a.json", """{"a":"text"}""");

        // Act
        var envelope = await harness.ReadJson.InvokeAsync(path: "a.json", json_path: "a[0]");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.JsonPathNotFound, envelope.Error.Code);
        Assert.Equal("a", envelope.Error.Detail["resolved_prefix"]);
        Assert.Equal("string", envelope.Error.Detail["kind"]);
    }

    [Fact]
    public async Task ReadJson_CaseMismatch_MissesAndHintsTheRealName()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("a.json", """{"foo":1}""");

        // Act
        var envelope = await harness.ReadJson.InvokeAsync(path: "a.json", json_path: "Foo");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.JsonPathNotFound, envelope.Error.Code);
        Assert.Contains("foo", (string[])envelope.Error.Detail["properties"]);
        Assert.DoesNotContain("foo", envelope.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadJson_LongKeyInHint_IsTruncatedWithAMarker()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        var longKey = new string('k', 150);
        harness.Write("a.json", $$"""{"{{longKey}}":1}""");

        // Act
        var envelope = await harness.ReadJson.InvokeAsync(path: "a.json", json_path: "missing");

        // Assert
        Assert.NotNull(envelope.Error);
        var hinted = Assert.Single((string[])envelope.Error.Detail["properties"]);
        Assert.Equal(103, hinted.Length);
        Assert.EndsWith("...", hinted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadJson_OversizedValue_ReportsValueTooLargeWithHint()
    {
        // Arrange
        using var harness = new TextSearchHarness(maxJsonValueBytes: 64);
        harness.Write("big.json", $$"""{"blob":{"text":"{{new string('x', 200)}}"},"small":1}""");

        // Act
        var envelope = await harness.ReadJson.InvokeAsync(path: "big.json", json_path: "blob");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.ValueTooLarge, envelope.Error.Code);
        Assert.Equal("value exceeds the configured value limit; pass a narrower json_path", envelope.Error.Message);
        Assert.True((int)envelope.Error.Detail["value_bytes"] > 64);
        Assert.Equal(64L, envelope.Error.Detail["limit"]);
        Assert.Equal(["text"], (string[])envelope.Error.Detail["properties"]);
    }

    [Fact]
    public async Task ReadJson_OversizedDocument_NarrowerPathStillSucceeds()
    {
        // Arrange
        using var harness = new TextSearchHarness(maxJsonValueBytes: 64);
        harness.Write("big.json", $$"""{"blob":"{{new string('x', 200)}}","small":7}""");

        // Act
        var envelope = await harness.ReadJson.InvokeAsync(path: "big.json", json_path: "small");

        // Assert
        Assert.Null(envelope.Error);
        var result = Assert.IsType<JsonValueResult>(Assert.Single(envelope.Results));
        Assert.Equal(7, result.Value.GetValue<int>());
    }

    [Fact]
    public async Task ReadJson_BinaryFile_IsRefused()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.WriteBytes("blob.bin", [0x00, 0x01, 0x02]);

        // Act
        var envelope = await harness.ReadJson.InvokeAsync(path: "blob.bin");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.IsBinary, envelope.Error.Code);
    }

    [Fact]
    public async Task ReadJson_MissingFile_ReportsNotFound()
    {
        // Arrange
        using var harness = new TextSearchHarness();

        // Act
        var envelope = await harness.ReadJson.InvokeAsync(path: "absent.json");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.NotFound, envelope.Error.Code);
    }

    [Fact]
    public async Task ReadJson_Denylisted_ReportsNotFoundNotDenied()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write(".env", """{"SECRET":"1"}""");

        // Act
        var envelope = await harness.ReadJson.InvokeAsync(path: ".env");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.NotFound, envelope.Error.Code);
    }

    [Fact]
    public async Task ReadJson_Gitignored_ReportsNotFound()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write(".gitignore", "hidden.json\n");
        harness.Write("hidden.json", """{"a":1}""");

        // Act
        var envelope = await harness.ReadJson.InvokeAsync(path: "hidden.json");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.NotFound, envelope.Error.Code);
    }

    [Fact]
    public async Task ReadJson_SecretShapedContent_IsWithheld()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("creds.json", """{"awsAccessKeyId":"AKIAIOSFODNN7EXAMPLE"}""");

        // Act
        var envelope = await harness.ReadJson.InvokeAsync(path: "creds.json", json_path: "awsAccessKeyId");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.WithheldSecret, envelope.Error.Code);
    }

    [Fact]
    public async Task ReadJson_CwdScope_ResolvesRelativeToTheProject()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("proj/config.json", """{"a":1}""");

        // Act
        var envelope = await harness.ReadJson.InvokeAsync(path: "config.json", cwd: harness.Dir("proj"), json_path: "a");

        // Assert
        Assert.Null(envelope.Error);
        var result = Assert.IsType<JsonValueResult>(Assert.Single(envelope.Results));
        Assert.Equal("config.json", result.Path);
        Assert.Equal(1, result.Value.GetValue<int>());
    }

    [Fact]
    public async Task ReadJson_PackageRoot_ReadsFromTheCache()
    {
        // Arrange
        using var harness = new TextSearchHarness(packageRoots: ["nuget"]);
        harness.WritePackage("nuget", "Pkg/1.0.0/pkg.json", """{"version":"1.0.0"}""");

        // Act
        var envelope = await harness.ReadJson.InvokeAsync(path: "pkg.json", cwd: "@nuget/Pkg/1.0.0", json_path: "version");

        // Assert
        Assert.Null(envelope.Error);
        var result = Assert.IsType<JsonValueResult>(Assert.Single(envelope.Results));
        Assert.Equal("1.0.0", result.Value.GetValue<string>());
    }

    [Fact]
    public async Task ReadJson_AbsoluteInRootPath_EchoesTheConfinedRelativePath()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("abs.json", """{"a":1}""");

        // Act
        var envelope = await harness.ReadJson.InvokeAsync(path: Path.Combine(harness.Root, "abs.json"));

        // Assert
        Assert.Null(envelope.Error);
        var result = Assert.IsType<JsonValueResult>(Assert.Single(envelope.Results));
        Assert.Equal("abs.json", result.Path);
        Assert.DoesNotContain(harness.Root, TextSearchHarness.ToJson(envelope), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadJson_EmptyPath_ReportsInvalidArgument()
    {
        // Arrange
        using var harness = new TextSearchHarness();

        // Act
        var envelope = await harness.ReadJson.InvokeAsync(path: " ");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.InvalidArgument, envelope.Error.Code);
    }
}