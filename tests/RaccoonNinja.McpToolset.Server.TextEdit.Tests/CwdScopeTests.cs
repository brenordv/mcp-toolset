using RaccoonNinja.McpToolset.Server.TextEdit.Errors;
using RaccoonNinja.McpToolset.Server.TextEdit.Models;
using RaccoonNinja.McpToolset.Server.TextEdit.Tests.TestSupport;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Tests;

/// <summary>
/// The base-root plus per-call <c>cwd</c> model on the write path: a <c>cwd</c> scopes and firewalls the
/// mutation to one project, an omitted <c>cwd</c> edits the whole base, every changed/journaled/undone path
/// is base-relative, and every out-of-bounds <c>cwd</c> is refused and counted. Also covers the additive
/// denylist, the default-ignore tier, and undo across two cwd-scoped batches from one base journal.
/// </summary>
public sealed class CwdScopeTests
{
    [Fact]
    public async Task Replace_ScopedCwd_ReportsBaseRelativePaths_AndNoWholeBaseMetric()
    {
        // Arrange
        using var harness = new TextEditHarness();
        harness.WriteText("proj/a.txt", "hello world");
        harness.WriteText("other/b.txt", "hello world");
        var cwd = harness.Dir("proj");

        // Act
        var envelope = await harness.Replace.InvokeAsync("world", "text", cwd: cwd);

        // Assert
        Assert.Null(envelope.Error);
        var result = Assert.IsType<MutationResult>(Assert.Single(envelope.Results));
        Assert.Equal(1, result.Changed);
        Assert.Equal("proj/a.txt", Assert.Single(result.Files).Path);
        Assert.Equal("hello text", harness.ReadText("proj/a.txt"));
        Assert.Equal("hello world", harness.ReadText("other/b.txt"));
        Assert.Equal(0L, (long)harness.Metrics.Summary()["whole_base_calls_total"]);
    }

    [Fact]
    public async Task Replace_OmittedCwd_EditsWholeBase_AndCountsWholeBaseMetric()
    {
        // Arrange
        using var harness = new TextEditHarness();
        harness.WriteText("proj-a/x.txt", "hello world");
        harness.WriteText("proj-b/y.txt", "hello world");

        // Act
        var envelope = await harness.Replace.InvokeAsync("world", "text", glob: "**/*.txt");

        // Assert
        Assert.Null(envelope.Error);
        var result = Assert.IsType<MutationResult>(Assert.Single(envelope.Results));
        Assert.Equal(2, result.Changed);
        Assert.Equal(["proj-a/x.txt", "proj-b/y.txt"], result.Files.Select(file => file.Path).Order(StringComparer.Ordinal));
        Assert.True((long)harness.Metrics.Summary()["whole_base_calls_total"] >= 1);
    }

    [Fact]
    public async Task Replace_CwdEscapingBase_IsRefusedAndCounted()
    {
        // Arrange
        using var harness = new TextEditHarness();
        harness.WriteText("a.txt", "hello world");
        var escaping = Path.Combine(harness.Root, "..");

        // Act
        var envelope = await harness.Replace.InvokeAsync("world", "text", cwd: escaping);

        // Assert
        Assert.NotNull(envelope.Error);
        Assert.Equal(ErrorCodes.InvalidArgument, envelope.Error.Code);
        var refusals = (IDictionary<string, object>)harness.Metrics.Summary()["refusals_total"];
        Assert.Contains("cwd_outside_base", refusals.Keys);
    }

    [Fact]
    public async Task Replace_ExplicitPathEscapingCwd_DoesNotWriteSiblingProject()
    {
        // Arrange
        using var harness = new TextEditHarness();
        harness.WriteText("projectA/keep.txt", "hello world");
        harness.WriteText("projectB/secret.txt", "hello world");
        var cwd = harness.Dir("projectA");

        // Act: an explicit path aimed at a sibling project, from within projectA's scope.
        var envelope = await harness.Replace.InvokeAsync("world", "text", cwd: cwd, paths: ["../projectB/secret.txt"]);

        // Assert
        Assert.Null(envelope.Error);
        var result = Assert.IsType<MutationResult>(Assert.Single(envelope.Results));
        Assert.Equal(0, result.Changed);
        Assert.Equal("hello world", harness.ReadText("projectB/secret.txt"));
    }

    [Fact]
    public async Task Replace_ScopedBatch_JournalRowsAreFinalizedNotPending()
    {
        // Arrange
        using var harness = new TextEditHarness();
        harness.WriteText("proj/a.txt", "hello world");
        var cwd = harness.Dir("proj");

        // Act
        var envelope = await harness.Replace.InvokeAsync("world", "text", cwd: cwd);
        var result = Assert.IsType<MutationResult>(Assert.Single(envelope.Results));

        // Assert: the single internal frame keeps FinalizeChanged matching, so no row is left pending
        // (a pending row would be restored by undo with no hash gate).
        Assert.NotNull(result.BatchId);
        var rows = harness.Journal.GetBatchFiles(result.BatchId.Value);
        Assert.Equal("proj/a.txt", Assert.Single(rows).Path);
        Assert.All(rows, row => Assert.False(string.IsNullOrEmpty(row.PostHash)));
    }

    [Fact]
    public async Task Replace_ExtraDeny_RefusesExtendedDenylistedFile()
    {
        // Arrange
        using var harness = new TextEditHarness(extraDeny: "*.secret");
        harness.WriteText("token.secret", "hello world");

        // Act
        var envelope = await harness.Replace.InvokeAsync("world", "text", paths: ["token.secret"]);

        // Assert: the extended denylist prunes the file during selection, so it is never changed.
        var result = Assert.IsType<MutationResult>(Assert.Single(envelope.Results));
        Assert.Equal(0, result.Changed);
        Assert.DoesNotContain(result.Files, file => file.Path == "token.secret" && file.Outcome == "changed");
        Assert.Equal("hello world", harness.ReadText("token.secret"));
    }

    [Fact]
    public async Task Normalize_DefaultIgnore_PrunesBuildDirOnWritePath()
    {
        // Arrange
        using var harness = new TextEditHarness();
        harness.WriteText("bin/generated.txt", "trailing   \n");
        harness.WriteText("src/keep.txt", "trailing   \n");

        // Act
        var envelope = await harness.Normalize.InvokeAsync(glob: "**/*.txt", trim_trailing_whitespace: true);

        // Assert
        var result = Assert.IsType<MutationResult>(Assert.Single(envelope.Results));
        Assert.DoesNotContain(result.Files, file => file.Path == "bin/generated.txt");
        Assert.Contains(result.Files, file => file.Path == "src/keep.txt" && file.Outcome == "changed");
    }

    [Fact]
    public async Task Undo_AcrossTwoCwdScopedBatches_FromOneBaseJournal()
    {
        // Arrange
        using var harness = new TextEditHarness();
        harness.WriteText("projectA/a.txt", "hello world");
        harness.WriteText("projectB/b.txt", "hello world");

        var batchA = Assert.IsType<MutationResult>(Assert.Single(
            (await harness.Replace.InvokeAsync("world", "text", cwd: harness.Dir("projectA"))).Results));
        var batchB = Assert.IsType<MutationResult>(Assert.Single(
            (await harness.Replace.InvokeAsync("world", "text", cwd: harness.Dir("projectB"))).Results));

        // Act
        var undoA = harness.Undoer.Undo(batchA.BatchId.Value);
        var undoB = harness.Undoer.Undo(batchB.BatchId.Value);

        // Assert: each batch's journaled path is base-relative and restores across projects from one journal.
        Assert.Equal(["projectA/a.txt"], undoA.Restored);
        Assert.Equal(["projectB/b.txt"], undoB.Restored);
        Assert.Equal("hello world", harness.ReadText("projectA/a.txt"));
        Assert.Equal("hello world", harness.ReadText("projectB/b.txt"));
    }

    [Fact]
    public async Task Replace_ScopedCwd_EchoesBaseRelativeScopeKey_NotAbsoluteCwd()
    {
        // Arrange
        using var harness = new TextEditHarness();
        harness.WriteText("proj/a.txt", "hello world");
        var cwd = harness.Dir("proj");

        // Act
        var envelope = await harness.Replace.InvokeAsync("world", "text", cwd: cwd);

        // Assert
        Assert.Equal("proj", envelope.FiltersApplied["cwd"]);
        Assert.DoesNotContain(harness.Root, (string)envelope.FiltersApplied["cwd"], StringComparison.Ordinal);
    }
}