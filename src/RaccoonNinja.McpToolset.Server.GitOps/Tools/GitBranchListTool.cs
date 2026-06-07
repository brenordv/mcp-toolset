using System.ComponentModel;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.GitOps.Envelope;
using RaccoonNinja.McpToolset.Server.GitOps.Parsers;
using RaccoonNinja.McpToolset.Server.GitOps.Security;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tools;

[McpServerToolType]
public sealed class GitBranchListTool(ToolCommon common)
{
    private const string Format =
        "%(refname)\x1f%(HEAD)\x1f%(objectname)\x1f%(upstream:short)\x1f%(contents:subject)";

    [McpServerTool(Name = "git_branch_list")]
    [Description("List local branches; include remotes when 'include_remote' is true.")]
    public Task<ResultEnvelope> InvokeAsync(
        [Description("Absolute working directory.")] string cwd,
        [Description("Include refs under refs/remotes as well.")] bool includeRemote = false,
        CancellationToken cancellationToken = default)
    {
        var ctx = common.MakeContext("git_branch_list");
        var holder = new RootHolder();
        return common.WrapAsync(ctx, holder, async () =>
        {
            var root = await common.ResolveAndLogAsync(cwd, ctx, holder, cancellationToken).ConfigureAwait(false);

            var positional = new List<string> { "refs/heads" };
            if (includeRemote) positional.Add("refs/remotes");

            var intent = new GitIntent
            {
                Subcommand = "for-each-ref",
                RepoRoot = root,
                Flags = [$"--format={Format}"],
                PositionalServerArgs = positional,
            };
            var result = await common.ExecuteAsync(intent, ctx, cancellationToken: cancellationToken).ConfigureAwait(false);
            var branches = BranchParser.Parse(result.Stdout);

            var filtersApplied = FiltersAppliedBuilder.Create()
                .Flag("include_remote", includeRemote)
                .Build();
            return ToolCommon.ListSuccess(branches, root, filtersApplied: filtersApplied, truncated: result.Truncated);
        });
    }
}