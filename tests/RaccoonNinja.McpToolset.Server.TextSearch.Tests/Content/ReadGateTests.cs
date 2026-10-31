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
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write(".gitignore", "appsettings.Production.json\n");
        harness.Write("proj/appsettings.Production.json", "{ \"secret\": true }");

        // Act
        var envelope = await harness.ReadLines.InvokeAsync(path: "appsettings.Production.json", cwd: harness.Dir("proj"));

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.NotFound, envelope.Error.Code);
    }

    [Fact]
    public async Task ReadLines_AgentIgnoredFile_IsRefused()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write(".cursorignore", "agent-only.json\n");
        harness.Write("agent-only.json", "{ \"secret\": true }");

        // Act
        var envelope = await harness.ReadLines.InvokeAsync(path: "agent-only.json");

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.NotFound, envelope.Error.Code);
    }

    [Fact]
    public async Task Inspect_AgentIgnoredFileInPaths_IsOmitted()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write(".cursorignore", "agent-only.json\n");
        harness.Write("agent-only.json", "{ }");
        harness.Write("ok.cs", "1");

        // Act
        var envelope = await harness.Inspect.InvokeAsync(paths: ["agent-only.json", "ok.cs"]);

        // Assert
        Assert.Equal(["ok.cs"], TextSearchHarness.Paths(envelope));
    }

    [Fact]
    public async Task ReadLines_FileInUnlistableDirectory_FailsClosedAsInternalError()
    {
        SkipUnlessUnixModesApply();

        // Arrange
        const string secretFile = "proj/secret.json";
        using var harness = new TextSearchHarness();
        harness.Write("proj/.gitignore", "secret.json\n");
        harness.Write(secretFile, "{ \"secret\": true }");
        var proj = Path.Combine(harness.Root, "proj");
        MakeUnlistable(proj);
        try
        {
            // Act
            var envelope = await harness.ReadLines.InvokeAsync(path: secretFile);

            // Assert
            Assert.Equal(ErrorCodes.InternalError, envelope.Error.Code);
            Assert.DoesNotContain(harness.Root, TextSearchHarness.ToJson(envelope), StringComparison.Ordinal);
        }
        finally
        {
            MakeListable(proj);
        }
    }

    #region Test Helpers

    private static TextSearchHarness SeedSecrets()
    {
        var harness = new TextSearchHarness();
        harness.Write("ok.cs", "const token = 1;");
        harness.Write(".env", "token=SECRET");
        harness.Write(".git/config", "[core]\n  token = SECRET");
        harness.Write(".ssh/id_rsa", "token PRIVATE KEY");
        return harness;
    }

    private static void SkipUnlessUnixModesApply()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes do not apply on Windows.");
        }

        if (Environment.IsPrivilegedProcess)
        {
            Assert.Skip("A privileged process bypasses directory mode bits.");
        }
    }

    private static void MakeUnlistable(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(directory, UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void MakeListable(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    #endregion
}