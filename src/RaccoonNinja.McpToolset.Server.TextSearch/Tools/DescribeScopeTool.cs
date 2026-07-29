using System.ComponentModel;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Files.Security;
using RaccoonNinja.McpToolset.Server.TextSearch.Configuration;
using RaccoonNinja.McpToolset.Server.TextSearch.Envelope;
using RaccoonNinja.McpToolset.Server.TextSearch.Models;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tools;

/// <summary>The <c>describe_scope</c> tool: reports the roots and every cap, with no absolute path.</summary>
[McpServerToolType]
public sealed class DescribeScopeTool(ToolCommon common, SearchConfig config, RootRegistry registry, ISecretDenylist denylist)
{
    /// <summary>Report the roots (name and kind), ignore kinds, denylist, encoding, column unit, and caps.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A single-item envelope carrying the scope description.</returns>
    [McpServerTool(Name = "describe_scope", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Report the sandbox this server is confined to: the allowed roots (name and kind, workspace or "
        + "package), the ignore-file kinds honored, the non-overridable secret denylist, the default "
        + "output encoding, the column unit, and every cap. Target one root by name, all workspace roots "
        + "by default, all package roots with root \"@packages\", or every root with \"@all\". Call this "
        + "first to learn the roots and limits.")]
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
                    MaxResults = config.MaxResults,
                    MaxMatchesPerFile = config.MaxMatchesPerFile,
                    MaxContextLines = config.MaxContextLines,
                    MaxLineSpan = config.MaxLineSpan,
                    RegexTimeoutMs = (int)config.RegexTimeout.TotalMilliseconds,
                    OperationBudgetMs = (int)config.OperationBudget.TotalMilliseconds,
                },
            };

            return Task.FromResult(ToolCommon.SingleSuccess(info));
        });
    }
}