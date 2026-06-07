using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.GitOps.Envelope;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Parsers;
using RaccoonNinja.McpToolset.Server.GitOps.Security;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tools;

[McpServerToolType]
public sealed class GitStashShowTool(ToolCommon common)
{
    [McpServerTool(Name = "git_stash_show")]
    [Description("Return the diff for stash@{index} as a DiffResult.")]
    public Task<ResultEnvelope> InvokeAsync(
        [Description("Absolute working directory.")] string cwd,
        [Description("Stash index (0 = most recent).")] int index = 0,
        CancellationToken cancellationToken = default)
    {
        var ctx = common.MakeContext("git_stash_show");
        var holder = new RootHolder();
        return common.WrapAsync(ctx, holder, async () =>
        {
            var root = await common.ResolveAndLogAsync(cwd, ctx, holder, cancellationToken).ConfigureAwait(false);
            if (index < 0)
                throw new RejectedArgumentException(
                    "stash index must be >= 0",
                    new Dictionary<string, object> { ["param"] = "index" });

            var stashRef = $"stash@{{{index.ToString(CultureInfo.InvariantCulture)}}}";

            var unifiedIntent = new GitIntent
            {
                Subcommand = "stash",
                SubSubcommand = "show",
                RepoRoot = root,
                Flags = ["-p"],
                VerifiedRefs = [stashRef],
            };
            var numstatIntent = new GitIntent
            {
                Subcommand = "stash",
                SubSubcommand = "show",
                RepoRoot = root,
                Flags = ["--numstat", "-z"],
                VerifiedRefs = [stashRef],
            };
            var unified = await common.ExecuteAsync(unifiedIntent, ctx, cancellationToken: cancellationToken).ConfigureAwait(false);
            var numstatResult = await common.ExecuteAsync(numstatIntent, ctx, cancellationToken: cancellationToken).ConfigureAwait(false);
            var files = DiffParser.ParseUnified(unified.Stdout);
            var numstat = DiffParser.ParseNumstatZ(numstatResult.Stdout);
            var diff = DiffParser.AssembleDiffResult(files, "ref_to_ref",
                unified.Truncated || numstatResult.Truncated, numstat);

            var filtersApplied = FiltersAppliedBuilder.Create()
                .Number("index", index)
                .Build();
            return ToolCommon.SingleSuccess(diff, root, filtersApplied: filtersApplied, truncated: diff.Truncated);
        });
    }
}