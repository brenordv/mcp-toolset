using System.ComponentModel;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.GitOps.Envelope;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Extensions;
using RaccoonNinja.McpToolset.Server.GitOps.Parsers;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Security;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tools;

[McpServerToolType]
public sealed class GitShowTool(ToolCommon common, IRefVerifier refVerifier)
{
    [McpServerTool(Name = "git_show")]
    [Description("Inspect a commit, or the commit an annotated tag points to: returns its metadata plus per-file diffs. A ref naming a tree or a blob is rejected.")]
    public Task<ResultEnvelope> InvokeAsync(
        [Description("Absolute working directory.")]
        string cwd,
        [Description("Ref to inspect (branch, tag, SHA, HEAD, HEAD~n, ...). A ref naming a tree or a blob, or a <rev>:<path> expression, is rejected; use git_file_at_ref for a file's content.")]
        string @ref = null,
        [Description("Restrict the diff section to these repo-relative paths.")]
        string[] paths = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = common.MakeContext("git_show");
        var holder = new RootHolder();
        return common.WrapAsync(ctx, holder, async () =>
        {
            var root = await common.ResolveAndLogAsync(cwd, ctx, holder, cancellationToken);
            if (RevPathExpression.IsRevPath(@ref))
            {
                throw new RejectedArgumentException(
                    "ref is a <rev>:<path> expression that names a file or tree, not a commit; use git_file_at_ref with ref and path to read a file",
                    new Dictionary<string, object> { ["param"] = "ref" });
            }

            var sha = await refVerifier.VerifyRequiredRefAsync(root, @ref, cancellationToken);
            var pathspecs = ToolCommon.ConfinePaths(root, paths);

            var metaIntent = new GitIntent
            {
                Subcommand = "log",
                RepoRoot = root,
                Flags = [$"--pretty=format:{LogParser.LogPrettyFormat}"],
                AttachedOptions = [new AttachedOption("--max-count", "1")],
                VerifiedRefs = [sha],
            };
            var metaResult = await common.ExecuteAsync(metaIntent, ctx, cancellationToken: cancellationToken);
            var commits = LogParser.Parse(metaResult.Stdout);
            if (commits.Count == 0)
            {
                throw new RejectedArgumentException(
                    "ref names a tree or a blob (or a tag of one), not a commit; use git_file_at_ref with ref and path to read a file",
                    new Dictionary<string, object> { ["param"] = "ref" });
            }

            var commit = commits[0];
            if (!RefVerifier.IsResolvedObjectName(commit.Hash))
            {
                throw new GitCommandException(
                    "log returned a malformed commit hash (an i18n.logOutputEncoding or i18n.commitEncoding setting can cause this)");
            }

            var patchIntent = new GitIntent
            {
                Subcommand = "show",
                RepoRoot = root,
                Flags = ["--format=", .. DiffParser.PatchShapeFlags],
                VerifiedRefs = [commit.Hash],
                Pathspecs = pathspecs,
            };
            var patchResult = await common.ExecuteAsync(patchIntent, ctx, cancellationToken: cancellationToken);
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