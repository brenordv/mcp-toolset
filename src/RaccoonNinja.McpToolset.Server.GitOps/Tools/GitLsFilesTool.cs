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
    [Description("Return the list of tracked paths (optionally restricted to a subset); with stage, return each index entry with its mode, object id, and stage.")]
    public Task<ResultEnvelope> InvokeAsync(
        [Description("Absolute working directory.")] string cwd,
        [Description("Restrict to these repo-relative paths.")] string[] paths = null,
        [Description("Include each index entry's mode, object id, and stage number (git ls-files --stage). Results become {path, mode, object_id, stage} objects instead of path strings.")] bool stage = false,
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
                Flags = stage ? ["-z", "--stage"] : ["-z"],
                Pathspecs = ToolCommon.ConfinePaths(root, paths),
            };
            var result = await common.ExecuteAsync(intent, ctx, cancellationToken: cancellationToken).ConfigureAwait(false);
            IReadOnlyList<object> entries = stage
                ? LsFilesParser.ParseStage(result.Stdout)
                : LsFilesParser.Parse(result.Stdout);

            var filtersApplied = FiltersAppliedBuilder.Create()
                .Number("paths_count", paths?.Length ?? 0)
                .Flag("stage", stage)
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