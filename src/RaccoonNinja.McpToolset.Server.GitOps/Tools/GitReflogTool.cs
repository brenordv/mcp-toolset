using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.GitOps.Envelope;
using RaccoonNinja.McpToolset.Server.GitOps.Parsers;
using RaccoonNinja.McpToolset.Server.GitOps.Security;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tools;

[McpServerToolType]
public sealed class GitReflogTool(ToolCommon common)
{
    [McpServerTool(Name = "git_reflog")]
    [Description("Return reflog entries: { hash, short_hash, selector, subject, when, relative_time }.")]
    public Task<ResultEnvelope> InvokeAsync(
        [Description("Absolute working directory.")] string cwd,
        [Description("Cap the number of entries returned. Default 50.")] int maxCount = 50,
        CancellationToken cancellationToken = default)
    {
        var ctx = common.MakeContext("git_reflog");
        var holder = new RootHolder();
        return common.WrapAsync(ctx, holder, async () =>
        {
            var root = await common.ResolveAndLogAsync(cwd, ctx, holder, cancellationToken).ConfigureAwait(false);

            var attached = new List<AttachedOption>
            {
                new("--max-count", maxCount.ToString(CultureInfo.InvariantCulture)),
            };
            var intent = new GitIntent
            {
                Subcommand = "reflog",
                SubSubcommand = "show",
                RepoRoot = root,
                Flags = [$"--format={ReflogParser.ReflogFormat}"],
                AttachedOptions = attached,
            };
            var result = await common.ExecuteAsync(intent, ctx, cancellationToken: cancellationToken).ConfigureAwait(false);
            var entries = ReflogParser.Parse(result.Stdout);

            var filtersApplied = FiltersAppliedBuilder.Create()
                .Number("max_count", maxCount)
                .Build();
            return ToolCommon.ListSuccess(entries, root, filtersApplied: filtersApplied, truncated: result.Truncated);
        });
    }
}