using System.ComponentModel;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.GitOps.Envelope;
using RaccoonNinja.McpToolset.Server.GitOps.Parsers;
using RaccoonNinja.McpToolset.Server.GitOps.Security;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tools;

[McpServerToolType]
public sealed class GitLsFilesTool(ToolCommon common)
{
    [McpServerTool(Name = "git_ls_files")]
    [Description("Return the list of tracked paths (optionally restricted to a subset).")]
    public Task<ResultEnvelope> InvokeAsync(
        [Description("Absolute working directory.")] string cwd,
        [Description("Restrict to these repo-relative paths.")] string[] paths = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = common.MakeContext("git_ls_files");
        var holder = new RootHolder();
        return common.WrapAsync(ctx, holder, async () =>
        {
            var root = await common.ResolveAndLogAsync(cwd, ctx, holder, cancellationToken).ConfigureAwait(false);

            var intent = new GitIntent
            {
                Subcommand = "ls-files",
                RepoRoot = root,
                Flags = ["-z"],
                Pathspecs = ToolCommon.ConfinePaths(root, paths),
            };
            var result = await common.ExecuteAsync(intent, ctx, cancellationToken: cancellationToken).ConfigureAwait(false);
            var entries = LsFilesParser.Parse(result.Stdout);

            var filtersApplied = FiltersAppliedBuilder.Create()
                .Number("paths_count", paths?.Length ?? 0)
                .Build();
            return ToolCommon.ListSuccess(
                entries,
                root,
                preFilterCount: entries.Count,
                filtersApplied: filtersApplied,
                truncated: result.Truncated);
        });
    }
}