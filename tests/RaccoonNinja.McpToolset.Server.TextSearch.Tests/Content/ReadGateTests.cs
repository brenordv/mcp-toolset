using RaccoonNinja.McpToolset.Server.TextSearch.Content;
using RaccoonNinja.McpToolset.Server.TextSearch.Errors;
using RaccoonNinja.McpToolset.Server.TextSearch.Tests.TestSupport;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tests.Content;

/// <summary>
/// The read gate must hold when enumeration is skipped: <c>paths[]</c> and a single-path read never
/// reach a denylisted or out-of-root file, so a blanket-approved server cannot be steered into reading
/// a secret into model context.
/// </summary>
public sealed class ReadGateTests
{
    private static readonly string[] SecretPaths = ["ok.cs", ".env", ".git/config", ".ssh/id_rsa"];

    [Fact]
    public async Task Inspect_PathsWithSecrets_OmitsDenylisted()
    {
        // Arrange
        using var harness = SeedSecrets();

        // Act
        var envelope = await harness.Inspect.InvokeAsync(paths: SecretPaths);

        // Assert
        Assert.Equal(["ok.cs"], TextSearchHarness.Paths(envelope));
    }

    [Fact]
    public async Task Search_PathsWithSecrets_MatchesOnlyAllowedFile()
    {
        // Arrange
        using var harness = SeedSecrets();

        // Act
        var envelope = await harness.Search.InvokeAsync(pattern: "token", paths: SecretPaths);

        // Assert
        var matches = envelope.Results.Cast<ContentMatch>().ToArray();
        Assert.NotEmpty(matches);
        Assert.All(matches, match => Assert.Equal("ok.cs", match.Path));
    }

    [Fact]
    public async Task ReadLines_GitConfig_IsRefused()
    {
        // Arrange
        using var harness = SeedSecrets();

        // Act
        var envelope = await harness.ReadLines.InvokeAsync(path: ".git/config");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.NotFound, envelope.Error.Code);
    }

    [Fact]
    public async Task ReadLines_LocalSettingsJson_IsRefused()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("local.settings.json", "{ \"Values\": { \"Key\": \"FAKE\" } }");

        // Act
        var envelope = await harness.ReadLines.InvokeAsync(path: "local.settings.json");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.NotFound, envelope.Error.Code);
    }

    [Fact]
    public async Task Inspect_LocalSettingsJsonInPaths_OmitsDenylisted()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("local.settings.json", "{ \"Values\": { } }");
        harness.Write("ok.cs", "1");

        // Act
        var envelope = await harness.Inspect.InvokeAsync(paths: ["local.settings.json", "ok.cs"]);

        // Assert
        Assert.Equal(["ok.cs"], TextSearchHarness.Paths(envelope));
    }

    [Fact]
    public async Task ReadLines_GitignoredFile_IsRefused()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write(".gitignore", "appsettings.Production.json\n");
        harness.Write("appsettings.Production.json", "{ \"ConnectionStrings\": { \"Db\": \"x\" } }");

        // Act
        var envelope = await harness.ReadLines.InvokeAsync(path: "appsettings.Production.json");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.NotFound, envelope.Error.Code);
    }

    [Fact]
    public async Task Inspect_GitignoredFileInPaths_IsOmitted()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write(".gitignore", "appsettings.Production.json\n");
        harness.Write("appsettings.Production.json", "{ }");
        harness.Write("ok.cs", "1");

        // Act
        var envelope = await harness.Inspect.InvokeAsync(paths: ["appsettings.Production.json", "ok.cs"]);

        // Assert
        Assert.Equal(["ok.cs"], TextSearchHarness.Paths(envelope));
    }

    [Fact]
    public async Task ReadLines_AncestorGitignore_HidesFileInScopedCwd()
    {
        // Arrange: the .gitignore sits at the base root, ABOVE the scoped cwd, so a walk rooted at the cwd
        // would not consult it. The base-anchored boundary must still refuse the read.
        using var harness = new TextSearchHarness();
        harness.Write(".gitignore", "appsettings.Production.json\n");
        harness.Write("proj/appsettings.Production.json", "{ \"secret\": true }");

        // Act
        var envelope = await harness.ReadLines.InvokeAsync(path: "appsettings.Production.json", cwd: harness.Dir("proj"));

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.NotFound, envelope.Error.Code);
    }

    private static TextSearchHarness SeedSecrets()
    {
        var harness = new TextSearchHarness();
        harness.Write("ok.cs", "const token = 1;");
        harness.Write(".env", "token=SECRET");
        harness.Write(".git/config", "[core]\n  token = SECRET");
        harness.Write(".ssh/id_rsa", "token PRIVATE KEY");
        return harness;
    }
}