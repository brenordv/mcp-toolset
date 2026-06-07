using System.ComponentModel;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.GitOps.Envelope;
using RaccoonNinja.McpToolset.Server.GitOps.Extensions;
using RaccoonNinja.McpToolset.Server.GitOps.Parsers;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Security;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tools;

[McpServerToolType]
public sealed class GitShowTool(ToolCommon common, IRefVerifier refVerifier)
{
    [McpServerTool(Name = "git_show")]
    [Description("Inspect a commit/tag/tree: returns its metadata plus per-file diffs.")]
    public Task<ResultEnvelope> InvokeAsync(
        [Description("Absolute working directory.")]
        string cwd,
        [Description("Ref to inspect (branch, tag, SHA, HEAD, HEAD~n, ...).")]
        string @ref = null,
        [Description("Restrict the diff section to these repo-relative paths.")]
        string[] paths = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = common.MakeContext("git_show");
        var holder = new RootHolder();
        return common.WrapAsync(ctx, holder, async () =>
        {
            var root = await common.ResolveAndLogAsync(cwd, ctx, holder, cancellationToken).ConfigureAwait(false);
            var sha = await refVerifier.VerifyRequiredRefAsync(root, @ref, cancellationToken).ConfigureAwait(false);
            var pathspecs = ToolCommon.ConfinePaths(root, paths);

            var metaIntent = new GitIntent
            {
                Subcommand = "log",
                RepoRoot = root,
                Flags = [$"--pretty=format:{LogParser.LogPrettyFormat}"],
                AttachedOptions = [new AttachedOption("--max-count", "1")],
                VerifiedRefs = [sha],
            };
            var metaResult = await common.ExecuteAsync(metaIntent, ctx, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var commits = LogParser.Parse(metaResult.Stdout);
            var commit = commits.Count > 0 ? commits[0] : null;

            var patchIntent = new GitIntent
            {
                Subcommand = "show",
                RepoRoot = root,
                Flags = ["--format="],
                VerifiedRefs = [sha],
                Pathspecs = pathspecs,
            };
            var patchResult = await common.ExecuteAsync(patchIntent, ctx, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var files = DiffParser.ParseUnified(patchResult.Stdout);

            var payload = new Dictionary<string, object> { ["commit"] = commit, ["files"] = files, };
            var filtersApplied = FiltersAppliedBuilder.Create()
                .Redact("ref", @ref)
                .Number("paths_count", paths?.Length ?? 0)
                .Build();
            return ToolCommon.SingleSuccess(
                payload,
                root,
                filtersApplied: filtersApplied,
                truncated: metaResult.Truncated || patchResult.Truncated);
        });
    }
}