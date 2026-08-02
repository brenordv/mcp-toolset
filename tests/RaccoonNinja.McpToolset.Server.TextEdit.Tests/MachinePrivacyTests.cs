using RaccoonNinja.McpToolset.Server.TextEdit.Content;
using RaccoonNinja.McpToolset.Server.TextEdit.Tests.TestSupport;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Tests;

public sealed class MachinePrivacyTests : IDisposable
{
    private readonly TextEditHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void Apply_ResultAndJournal_CarryNoAbsolutePath()
    {
        // Arrange
        _harness.WriteText("dir/a.txt", "hello world");

        // Act
        var outcome = _harness.Apply("replace_text", new Replacer("world", "text-edit", false, false, _harness.Config), "dir/a.txt");

        // Assert
        Assert.All(outcome.Files, file => Assert.DoesNotContain(_harness.Root, file.Path, StringComparison.Ordinal));
        var journalPaths = _harness.Journal.GetBatchFiles(outcome.BatchId.Value).Select(row => row.Path);
        Assert.All(journalPaths, path => Assert.DoesNotContain(_harness.Root, path, StringComparison.Ordinal));
    }

    [Fact]
    public void DryRun_Diff_CarriesNoAbsolutePath()
    {
        // Arrange
        _harness.WriteText("dir/a.txt", "hello world");

        // Act
        var outcome = _harness.Writer.Apply(
            "replace_text",
            ["dir/a.txt"],
            new Replacer("world", "text-edit", false, false, _harness.Config),
            "t",
            expectedMatchCount: null,
            dryRun: true,
            sourceEncoding: null,
            skippedSymlinks: 0,
            truncated: false,
            _harness.Confinement,
            CancellationToken.None);

        // Assert
        Assert.DoesNotContain(_harness.Root, outcome.Files[0].Diff, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Replace_AbsoluteCwd_EnvelopeCarriesNoAbsolutePath()
    {
        // Arrange
        _harness.WriteText("proj/a.txt", "hello world");
        var cwd = _harness.Dir("proj");

        // Act
        var envelope = await _harness.Replace.InvokeAsync("world", "text-edit", cwd: cwd);

        // Assert
        Assert.Null(envelope.Error);
        var json = System.Text.Json.JsonSerializer.Serialize(envelope);
        Assert.DoesNotContain(_harness.Root, json, StringComparison.Ordinal);
        Assert.DoesNotContain(_harness.Root.Replace("\\", "\\\\", StringComparison.Ordinal), json, StringComparison.Ordinal);
    }
}