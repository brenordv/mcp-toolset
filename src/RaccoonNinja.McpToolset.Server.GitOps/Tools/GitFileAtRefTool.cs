using System.ComponentModel;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.GitOps.Envelope;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Extensions;
using RaccoonNinja.McpToolset.Server.GitOps.Models;
using RaccoonNinja.McpToolset.Server.GitOps.Parsers;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Security;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tools;

/// <summary>
/// Returns one tracked file's content as of a ref. Two passes: <c>ls-tree -z -l</c> locates the blob
/// (and proves the path names a file at that ref), then <c>show --format=</c> prints the blob. The
/// path stays a confined pathspec behind <c>--</c> and never enters a revision expression; only
/// object names checked by <see cref="RefVerifier"/> become positionals.
/// </summary>
[McpServerToolType]
public sealed class GitFileAtRefTool(ToolCommon common, IRefVerifier refVerifier)
{
    [McpServerTool(Name = "git_file_at_ref")]
    [Description("Return one tracked file's content as of a ref (the equivalent of git show <ref>:<path>), with its mode, blob id, and size. Works for files deleted since. ref defaults to HEAD. Committed content is returned as-is; git-ops has no secret denylist.")]
    public Task<ResultEnvelope> InvokeAsync(
        [Description("Absolute working directory.")]
        string cwd,
        [Description("Repo-relative path of the file at that ref, case-sensitive exactly as git stores it.")]
        string path,
        [Description("Commit-ish to read from (branch, tag, SHA, HEAD~n, ...). Default HEAD. Do not put the path here; use the path argument.")]
        string @ref = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = common.MakeContext("git_file_at_ref");
        var holder = new RootHolder();
        return common.WrapAsync(ctx, holder, async () =>
        {
            var root = await common.ResolveAndLogAsync(cwd, ctx, holder, cancellationToken);
            var confined = ValidateAndConfine(root, path, @ref);
            var sha = await refVerifier
                .VerifyRequiredRefAsync(root, string.IsNullOrWhiteSpace(@ref) ? "HEAD" : @ref, cancellationToken);

            var treeIntent = new GitIntent
            {
                Subcommand = "ls-tree",
                RepoRoot = root,
                Flags = ["-z", "-l"],
                VerifiedRefs = [sha],
                Pathspecs = [Pathspec.Literal(confined)],
            };
            var treeResult = await common.ExecuteAsync(
                treeIntent,
                ctx,
                stderrClassifier: ClassifyNotATreeObject,
                cancellationToken: cancellationToken);
            var entry = SelectBlob(LsTreeParser.Parse(treeResult.Stdout), confined);

            var blobIntent = new GitIntent
            {
                Subcommand = "show",
                RepoRoot = root,
                Flags = ["--format="],
                VerifiedRefs = [entry.ObjectId],
            };
            var showResult = await common.ExecuteAsync(blobIntent, ctx, cancellationToken: cancellationToken);

            var bytes = showResult.Stdout;
            var isBinary = TextDecoding.IsLikelyBinary(bytes);
            var payload = new FileAtRef
            {
                Path = confined,
                RefSha = sha,
                Mode = entry.Mode,
                ObjectId = entry.ObjectId,
                Size = entry.Size,
                IsBinary = isBinary,
                Content = isBinary ? null : TextDecoding.Decode(bytes),
            };
            var filtersApplied = FiltersAppliedBuilder.Create()
                .Redact("ref", @ref)
                .Redact("path", path)
                .Build();
            return ToolCommon.SingleSuccess(
                payload,
                root,
                filtersApplied: filtersApplied,
                truncated: showResult.Truncated || treeResult.Truncated);
        });
    }

    /// <summary>
    /// Map git's <c>not a tree object</c> stderr (printed when the ref resolves to a blob) to a
    /// <see cref="RejectedArgumentException"/> on <c>ref</c>; returns <c>null</c> for any other stderr so
    /// the caller falls back to the generic command error.
    /// </summary>
    /// <param name="stderr">The decoded git stderr from the failed <c>ls-tree</c>.</param>
    /// <returns>The classified exception, or <c>null</c>.</returns>
    public static GitCheckException ClassifyNotATreeObject(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return null;
        }

        return stderr.Contains("not a tree object", StringComparison.Ordinal)
            ? Rejected("ref must name a commit or tree, not a blob", "ref")
            : null;
    }

    /// <summary>Reject an empty path, a directory-shaped path, a path inside <c>ref</c>, or the repository root; return the confined path.</summary>
    private static string ValidateAndConfine(string root, string path, string reference)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw Rejected("path must be a non-empty repo-relative file path", "path");
        }

        if (path.EndsWith('/') || path.EndsWith('\\'))
        {
            throw Rejected("path must name a file, not a directory", "path");
        }

        if (RevPathExpression.IsRevPath(reference))
        {
            throw Rejected("ref must be a commit-ish; pass the file in path, not inside ref", "ref");
        }

        var confined = PathConfinement.Confine(root, path);
        if (confined == ".")
        {
            throw Rejected("path must name a file, not the repository root", "path");
        }

        return confined;
    }

    /// <summary>Pick the entry whose path is exactly <paramref name="confined"/> and require it to be a blob with a well-formed object id.</summary>
    private static LsTreeEntry SelectBlob(IReadOnlyList<LsTreeEntry> entries, string confined)
    {
        var entry = entries.FirstOrDefault(e => string.Equals(e.Path, confined, StringComparison.Ordinal));
        if (entry is null)
        {
            throw new PathNotFoundException(
                "path does not exist at that ref",
                new Dictionary<string, object> { ["param"] = "path" });
        }

        if (entry.ObjectType != "blob")
        {
            throw Rejected("path does not name a file at that ref", "path");
        }

        if (!RefVerifier.IsResolvedObjectName(entry.ObjectId))
        {
            throw new GitCommandException("ls-tree returned a malformed object id");
        }

        return entry;
    }

    private static RejectedArgumentException Rejected(string message, string param)
        => new(message, new Dictionary<string, object> { ["param"] = param });
}