using RaccoonNinja.McpToolset.Server.TextSearch.Envelope;
using RaccoonNinja.McpToolset.Server.TextSearch.Tests.TestSupport;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tests;

/// <summary>No absolute path may reach model context, on the success channel or the failure channel.</summary>
public sealed class MachinePrivacyTests
{
    [Fact]
    public async Task Search_SuccessEnvelope_CarriesNoAbsolutePath()
    {
        using var harness = new TextSearchHarness();
        harness.Write("src/a.txt", "find me here");

        var envelope = await harness.Search.InvokeAsync(pattern: "me", glob: "**/*.txt");

        Assert.NotEmpty(envelope.Results);
        AssertNoAbsolutePath(harness.Root, envelope);
    }

    [Fact]
    public async Task ReadLines_LockedFile_FailsWithoutLeakingAbsolutePath()
    {
        if (!OperatingSystem.IsWindows())
        {
            // Exclusive-lock sharing semantics that force an IOException are Windows-specific.
            Assert.Skip("exclusive file locking is Windows-specific");
        }

        using var harness = new TextSearchHarness();
        harness.Write("locked.txt", "line one\nline two");
        var full = Path.Combine(harness.Root, "locked.txt");

        using (File.Open(full, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var envelope = await harness.ReadLines.InvokeAsync(path: "locked.txt");

            // The IO exception carries the absolute path in its message; none of it may reach the envelope.
            Assert.NotNull(envelope.Error);
            AssertNoAbsolutePath(harness.Root, envelope);
        }
    }

    private static void AssertNoAbsolutePath(string root, ResultEnvelope envelope)
    {
        var json = TextSearchHarness.ToJson(envelope);
        Assert.DoesNotContain(root, json, StringComparison.Ordinal);
        Assert.DoesNotContain(root.Replace("\\", "\\\\", StringComparison.Ordinal), json, StringComparison.Ordinal);
    }
}