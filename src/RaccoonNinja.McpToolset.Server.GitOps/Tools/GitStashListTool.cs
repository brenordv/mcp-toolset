using System.ComponentModel;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.GitOps.Envelope;
using RaccoonNinja.McpToolset.Server.GitOps.Parsers;
using RaccoonNinja.McpToolset.Server.GitOps.Security;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tools;

[McpServerToolType]
public sealed class GitStashListTool(ToolCommon common)
{
    [McpServerTool(Name = "git_stash_list")]
    [Description("List git stash entries.")]
    public Task<ResultEnvelope> InvokeAsync(
        [Description("Absolute working directory.")] string cwd,
        CancellationToken cancellationToken = default)
    {
        var ctx = common.MakeContext("git_stash_list");
        var holder = new RootHolder();
        return common.WrapAsync(ctx, holder, async () =>
        {
            var root = await common.ResolveAndLogAsync(cwd, ctx, holder, cancellationToken).ConfigureAwait(false);
            var intent = new GitIntent
            {
                Subcommand = "stash",
                SubSubcommand = "list",
                RepoRoot = root,
                Flags = ["--format=%gd%x1f%gs%x1f%at"],
            };
            var result = await common.ExecuteAsync(intent, ctx, cancellationToken: cancellationToken).ConfigureAwait(false);
            var entries = StashListParser.Parse(result.Stdout);
            var items = new List<object>(entries.Count);
            items.AddRange(entries);
            return ToolCommon.ListSuccess(items, root, truncated: result.Truncated);
        });
    }
}