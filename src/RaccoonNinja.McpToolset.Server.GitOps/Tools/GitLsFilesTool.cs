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
        [Description("Literal repo-relative paths (a file, or a directory and its subtree). Wildcards (* ?) and pathspec magic are rejected here; use globs and excludePaths.")] string[] paths = null,
        [Description("Include each index entry's mode, object id, and stage number (git ls-files --stage). Results become {path, mode, object_id, stage} objects instead of path strings.")] bool stage = false,
        [Description("Wildcard pathspecs, git's default matching: '*' and '?' also match '/', so '*.json' matches at any depth and 'projects/*/package.json' works. Repo-relative, forward slashes, no leading ':' or '-', no '..'.")] string[] globs = null,
        [Description("Paths or wildcard patterns to exclude from the listing (git ':(exclude)'). A directory excludes its subtree. Works alone, or together with paths and globs.")] string[] excludePaths = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = common.MakeContext("git_ls_files");
        var holder = new RootHolder();
        return common.WrapAsync(ctx, holder, async () =>
        {
            var root = await common.ResolveAndLogAsync(cwd, ctx, holder, cancellationToken);

            var intent = new GitIntent
            {
                Subcommand = "ls-files",
                RepoRoot = root,
                Flags = stage ? ["-z", "--stage"] : ["-z"],
                Pathspecs =
                [
                    .. ToolCommon.ConfinePaths(root, paths),
                    .. ToolCommon.GlobPathspecs(globs),
                    .. ToolCommon.ExcludePathspecs(excludePaths),
                ],
            };
            var result = await common.ExecuteAsync(intent, ctx, cancellationToken: cancellationToken);
            IReadOnlyList<object> entries = stage
                ? LsFilesParser.ParseStage(result.Stdout)
                : LsFilesParser.Parse(result.Stdout);

            var filtersApplied = FiltersAppliedBuilder.Create()
                .Number("paths_count", paths?.Length ?? 0)
                .Number("globs_count", globs?.Length ?? 0)
                .Number("exclude_paths_count", excludePaths?.Length ?? 0)
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