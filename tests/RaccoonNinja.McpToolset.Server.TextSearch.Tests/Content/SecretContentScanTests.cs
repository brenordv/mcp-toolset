using RaccoonNinja.McpToolset.Server.TextSearch.Errors;
using RaccoonNinja.McpToolset.Server.TextSearch.Tests.TestSupport;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tests.Content;

/// <summary>
/// Content-based secret detection: a file whose content matches a detector is withheld from every content
/// read (read_lines, search, inspect) regardless of its name, while it still lists in find_files (the name
/// is not the secret). The ignore boundary runs first, so an ignored file is disguised as NotFound.
/// </summary>
public sealed class SecretContentScanTests
{
    private const string SecretLine = "aws_key = \"AKIAIOSFODNN7EXAMPLE\"";

    [Fact]
    public async Task ReadLines_SecretContent_IsWithheld()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("config.cs", SecretLine);

        // Act
        var envelope = await harness.ReadLines.InvokeAsync(path: "config.cs");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.WithheldSecret, envelope.Error.Code);
    }

    [Fact]
    public async Task Search_SecretContent_NeverEchoesTheSecret()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("config.cs", SecretLine);
        harness.Write("notes.txt", "aws_key is documented here without a value");

        // Act
        var envelope = await harness.Search.InvokeAsync(pattern: "aws_key");

        // Assert
        Assert.Null(envelope.Error);
        Assert.DoesNotContain("AKIAIOSFODNN7EXAMPLE", TextSearchHarness.ToJson(envelope));
        Assert.DoesNotContain("config.cs", TextSearchHarness.Paths(envelope));
        Assert.Contains("notes.txt", TextSearchHarness.Paths(envelope));
    }

    [Fact]
    public async Task Inspect_SecretContent_IsOmitted()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("config.cs", SecretLine);
        harness.Write("ok.cs", "1");

        // Act
        var envelope = await harness.Inspect.InvokeAsync(paths: ["config.cs", "ok.cs"]);

        // Assert
        Assert.Equal(["ok.cs"], TextSearchHarness.Paths(envelope));
    }

    [Fact]
    public async Task Find_SecretContent_StillListsBecauseNameIsNotTheSecret()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("config.cs", SecretLine);

        // Act
        var envelope = await harness.Find.InvokeAsync(glob: "*.cs");

        // Assert
        Assert.Contains("config.cs", TextSearchHarness.Paths(envelope));
    }

    [Fact]
    public async Task ReadLines_ScanningDisabled_ReturnsFile()
    {
        // Arrange
        using var harness = new TextSearchHarness(secretScan: false);
        harness.Write("config.cs", SecretLine);

        // Act
        var envelope = await harness.ReadLines.InvokeAsync(path: "config.cs");

        // Assert
        Assert.Null(envelope.Error);
    }

    [Fact]
    public async Task ReadLines_IgnoredSecretFile_IsNotFoundNotWithheldSecret()
    {
        // Arrange: the ignore check runs before the content scan, so an ignored file is disguised as
        // NotFound and never reveals, via a distinct WithheldSecret code, that it exists and looks secret.
        using var harness = new TextSearchHarness();
        harness.Write(".gitignore", "config.cs\n");
        harness.Write("config.cs", SecretLine);

        // Act
        var envelope = await harness.ReadLines.InvokeAsync(path: "config.cs");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.NotFound, envelope.Error.Code);
    }
}