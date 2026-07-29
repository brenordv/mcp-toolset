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
        using var harness = SeedSecrets();

        var envelope = await harness.Inspect.InvokeAsync(paths: SecretPaths);

        Assert.Equal(["ok.cs"], TextSearchHarness.Paths(envelope));
    }

    [Fact]
    public async Task Search_PathsWithSecrets_MatchesOnlyAllowedFile()
    {
        using var harness = SeedSecrets();

        var envelope = await harness.Search.InvokeAsync(pattern: "token", paths: SecretPaths);

        var matches = envelope.Results.Cast<ContentMatch>().ToArray();
        Assert.NotEmpty(matches);
        Assert.All(matches, match => Assert.Equal("ok.cs", match.Path));
    }

    [Fact]
    public async Task ReadLines_GitConfig_IsRefused()
    {
        using var harness = SeedSecrets();

        var envelope = await harness.ReadLines.InvokeAsync(path: ".git/config");

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