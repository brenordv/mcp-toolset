using System.ComponentModel;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.TextSearch.Configuration;
using RaccoonNinja.McpToolset.Server.TextSearch.Envelope;
using RaccoonNinja.McpToolset.Server.TextSearch.Models;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tools;

/// <summary>The <c>describe_scope</c> tool: reports the base root, scope model, ignore tiers, denylist, and caps, with no absolute path.</summary>
[McpServerToolType]
public sealed class DescribeScopeTool(ToolCommon common, SearchConfig config, ScopeResolver resolver)
{
    private const string ScopeModelDescription =
        "Pass cwd (an absolute working directory inside the base root) to scope a call to one project; input "
        + "and output paths are then relative to cwd. Omit cwd to search the whole base root, the heavy path, "
        + "with base-relative paths. Ignore tiers (the built-in default set, .gitignore, .mcpignore) between "
        + "the base root and a scoped cwd are not consulted, so a scoped call can surface a non-secret file a "
        + "parent .gitignore would hide; the secret denylist is independent and always applies.";

    /// <summary>Report the base root, scope model, ignore tiers, denylist, encoding, column unit, and every cap.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A single-item envelope carrying the scope description.</returns>
    [McpServerTool(Name = "describe_scope", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Report the sandbox this server is confined to: the base root (by name only), how cwd scoping and "
        + "cwd-relative paths work, the default ignore tier and the project ignore-file kinds honored, the "
        + "non-overridable secret denylist, the default output encoding, the column unit, and every cap. Call "
        + "this first to learn the scope model and limits.")]
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
                IgnoreFiles = [".gitignore", ".mcpignore"],
                Denylist = [.. resolver.Denylist.DescribePatterns()],
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