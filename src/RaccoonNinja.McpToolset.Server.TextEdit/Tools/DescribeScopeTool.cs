using System.ComponentModel;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Files.Security;
using RaccoonNinja.McpToolset.Server.TextEdit.Configuration;
using RaccoonNinja.McpToolset.Server.TextEdit.Envelope;
using RaccoonNinja.McpToolset.Server.TextEdit.Models;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Tools;

/// <summary>The <c>describe_scope</c> tool: reports the single root, the denylist, the caps, and the journal retention, with no absolute path.</summary>
[McpServerToolType]
public sealed class DescribeScopeTool(ToolCommon common, EditConfig config, RootRegistry registry, ISecretDenylist denylist)
{
    /// <summary>Report the editable root, the ignore-file kinds, the denylist, the default encoding, the column unit, and every cap.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A single-item envelope carrying the scope description.</returns>
    [McpServerTool(Name = "describe_scope", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Report the sandbox this server may edit: the single root (name and kind), the ignore-file kinds "
        + "honored on the write path, the non-overridable secret denylist, the default output encoding, the "
        + "column unit, the journal retention, and every cap. Call this first to learn the root and limits.")]
    public Task<ResultEnvelope> InvokeAsync(CancellationToken cancellationToken = default)
    {
        var ctx = common.MakeContext("describe_scope");
        return common.WrapAsync(ctx, () =>
        {
            var info = new ScopeInfo
            {
                Roots = registry.Describe(),
                IgnoreFiles = [".gitignore", ".mcpignore"],
                DenylistPatterns = [.. denylist.DescribePatterns()],
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