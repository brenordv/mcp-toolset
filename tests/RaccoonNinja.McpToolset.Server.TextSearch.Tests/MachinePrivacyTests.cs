using RaccoonNinja.McpToolset.Server.TextSearch.Envelope;
using RaccoonNinja.McpToolset.Server.TextSearch.Tests.TestSupport;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tests;

/// <summary>No absolute path may reach model context, on the success channel or the failure channel.</summary>
public sealed class MachinePrivacyTests
{
    [Fact]
    public async Task Search_SuccessEnvelope_CarriesNoAbsolutePath()
    {
        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("src/a.txt", "find me here");

        // Act
        var envelope = await harness.Search.InvokeAsync(pattern: "me", glob: "**/*.txt");

        // Assert
        Assert.NotEmpty(envelope.Results);
        AssertNoAbsolutePath(harness.Root, envelope);
    }

    [Fact]
    public async Task ReadLines_LockedFile_FailsWithoutLeakingAbsolutePath()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("exclusive file locking is Windows-specific");
        }

        // Arrange
        using var harness = new TextSearchHarness();
        harness.Write("locked.txt", "line one\nline two");
        var full = Path.Combine(harness.Root, "locked.txt");

        using (File.Open(full, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            // Act
            var envelope = await harness.ReadLines.InvokeAsync(path: "locked.txt");

            // Assert
            Assert.NotNull(envelope.Error);
            AssertNoAbsolutePath(harness.Root, envelope);
        }
    }

    [Fact]
    public async Task Search_PackageScope_SuccessEnvelopeCarriesNoAbsoluteCachePath()
    {
        // Arrange
        using var harness = new TextSearchHarness(packageRoots: ["nuget"]);
        harness.WritePackage("nuget", "pkg/a.txt", "find me here");

        // Act
        var envelope = await harness.Search.InvokeAsync(pattern: "me", glob: "**/*.txt", cwd: "@nuget");

        // Assert
        Assert.NotEmpty(envelope.Results);
        AssertNoAbsolutePath(harness.PackageDir("nuget"), envelope);
    }

    private static void AssertNoAbsolutePath(string root, ResultEnvelope envelope)
    {
        var json = TextSearchHarness.ToJson(envelope);
        Assert.DoesNotContain(root, json, StringComparison.Ordinal);
        Assert.DoesNotContain(root.Replace("\\", "\\\\", StringComparison.Ordinal), json, StringComparison.Ordinal);
    }
}