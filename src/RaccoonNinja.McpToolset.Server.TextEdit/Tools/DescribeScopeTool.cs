using System.ComponentModel;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Files.Selection;
using RaccoonNinja.McpToolset.Server.TextEdit.Configuration;
using RaccoonNinja.McpToolset.Server.TextEdit.Envelope;
using RaccoonNinja.McpToolset.Server.TextEdit.Models;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Tools;

/// <summary>The <c>describe_scope</c> tool: reports the base root, scope model, ignore tiers, denylist, journal retention, and caps, with no absolute path.</summary>
[McpServerToolType]
public sealed class DescribeScopeTool(ToolCommon common, EditConfig config, ScopeResolver resolver)
{
    private const string ScopeModelDescription =
        "Pass cwd (an absolute working directory inside the base root) to scope a call to one project; "
        + "explicit paths are then relative to cwd and confined to it, so a scoped edit cannot write outside "
        + "its project. Omit cwd to edit across the whole base root. Reported, journaled, and undoable paths "
        + "are always relative to the base root (a batch is base-scoped), so undo and list_recent_batches are "
        + "base-global, not cwd-scoped. Ignore tiers (the built-in default set and the project ignore files in ignore_files) "
        + "between the base root and a scoped cwd are not consulted; the secret denylist is independent and "
        + "always applies.";

    /// <summary>Report the base root, scope model, ignore tiers, denylist, encoding, column unit, journal retention, and every cap.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A single-item envelope carrying the scope description.</returns>
    [McpServerTool(Name = "describe_scope", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Report the sandbox this server may edit: the base root (by name only), how cwd scoping works "
        + "(cwd-relative input, base-relative reporting, base-global undo), the default ignore tier and the "
        + "project ignore-file kinds honored, the non-overridable secret denylist, the default output "
        + "encoding, the column unit, the journal retention, and every cap. Call this first to learn the "
        + "scope model and limits.")]
    public Task<ResultEnvelope> InvokeAsync(CancellationToken cancellationToken = default)
    {
        var ctx = common.MakeContext("describe_scope");
        return common.WrapAsync(ctx, () =>
        {
            var info = new ScopeInfo
            {
                BaseRoot = resolver.BaseRootName,
                ScopeModel = ScopeModelDescription,
                DefaultIgnore = resolver.DefaultIgnorePatterns,
                IgnoreFiles = [.. IgnoreRules.IgnoreFileNames],
                DenylistPatterns = [.. resolver.Denylist.DescribePatterns()],
                DefaultEncoding = "utf-8",
                ColumnUnit = "utf-16 code units",
                DenylistedOmitted = true,
                Caps = new ScopeCaps
                {
                    MaxFilesDefault = config.MaxFilesDefault,
                    MaxFilesCeiling = config.MaxFilesCeiling,
                    MaxFileBytes = config.MaxFileBytes,
                    RegexTimeoutMs = (int)config.RegexTimeout.TotalMilliseconds,
                    OperationBudgetMs = (int)config.OperationBudget.TotalMilliseconds,
                    RewriteConfidence = config.RewriteConfidence,
                    PatternLengthCap = config.PatternLengthCap,
                    JournalRetentionBatches = config.JournalRetentionBatches,
                    JournalRetentionHours = config.JournalRetentionHours,
                },
            };

            return Task.FromResult(ToolCommon.SingleSuccess(info));
        });
    }
}