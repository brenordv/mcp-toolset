using System.ComponentModel;
using System.Globalization;
using System.Text;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.GitOps.Envelope;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Extensions;
using RaccoonNinja.McpToolset.Server.GitOps.Models;
using RaccoonNinja.McpToolset.Server.GitOps.Parsers;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Security;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tools;

[McpServerToolType]
public sealed class GitBlameTool(ToolCommon common, IRefVerifier refVerifier)
{
    /// <summary>The id git blame prints for lines not committed yet.</summary>
    private const string UncommittedCommitHash = "0000000000000000000000000000000000000000";

    private const string AuthorPassCutMessage = "the blamed commits' author pass hit the output cap";

    [McpServerTool(Name = "git_blame")]
    [Description("Return per-line author/commit/timestamp for a file, optionally restricted to a line range. author_name is the author recorded in the commit; mailmap_author_name, present only when the repository's mailmap (.mailmap, mailmap.file, or mailmap.blob) shows that author under another name, is that name.")]
    public Task<ResultEnvelope> InvokeAsync(
        [Description("Absolute working directory.")] string cwd,
        [Description("Repo-relative path to the file to blame.")] string path = null,
        [Description("Start line (1-based, inclusive).")] int? lineStart = null,
        [Description("End line (1-based, inclusive). Must be >= lineStart when supplied.")] int? lineEnd = null,
        [Description("Optional ref (branch, tag, SHA) to blame at instead of HEAD.")] string @ref = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = common.MakeContext("git_blame");
        var holder = new RootHolder();
        return common.WrapAsync(ctx, holder, async () =>
        {
            var root = await common.ResolveAndLogAsync(cwd, ctx, holder, cancellationToken);
            var rel = PathConfinement.Confine(root, path ?? string.Empty);
            var verifiedRefs = await refVerifier.VerifyOptionalRefsAsync(root, [@ref], cancellationToken);

            var attached = new List<AttachedOption>();
            (int Start, int End)? lineRange = null;
            if (lineStart.HasValue || lineEnd.HasValue)
            {
                var start = lineStart ?? 0;
                var end = lineEnd ?? 0;
                if (start <= 0 || end < start)
                    throw new RejectedArgumentException(
                        "line_range must be a positive (start <= end) tuple",
                        new Dictionary<string, object> { ["param"] = "line_range" });
                attached.Add(new AttachedOption("-L", $"{start.ToString(CultureInfo.InvariantCulture)},{end.ToString(CultureInfo.InvariantCulture)}"));
                lineRange = (start, end);
            }

            var intent = new GitIntent
            {
                Subcommand = "blame",
                RepoRoot = root,
                Flags = ["--porcelain"],
                AttachedOptions = attached,
                VerifiedRefs = verifiedRefs,
                // git blame takes a file argument, not a pathspec; a magic prefix would become part of the filename.
                Pathspecs = [Pathspec.FileArgument(rel)],
            };
            var result = await common.ExecuteAsync(intent, ctx, cancellationToken: cancellationToken);
            var lines = BlameParser.Parse(result.Stdout);
            lines = await ReadRawAuthorsAsync(root, lines, ctx, cancellationToken);

            var filtersApplied = FiltersAppliedBuilder.Create()
                .Redact("path", path)
                .Redact("ref", @ref)
                .Optional("line_range", lineRange.HasValue ? new[] { lineRange.Value.Start, lineRange.Value.End } : null)
                .Build();
            return ToolCommon.ListSuccess(lines, root, filtersApplied: filtersApplied, truncated: result.Truncated);
        });
    }

    /// <summary>
    /// Read the recorded author of each committed line's commit with one <c>git log --no-walk --stdin</c>, whose ids go on
    /// standard input, so the argv stays the same size however many commits the file has; git blame maps every author
    /// through the repository's mailmap and has no switch to stop it.
    /// </summary>
    /// <param name="root">The resolved repository root.</param>
    /// <param name="lines">The lines the porcelain blame parsed.</param>
    /// <param name="ctx">The call context; the pass logs under the same tool and call id.</param>
    /// <param name="cancellationToken">Token to cancel the pass.</param>
    /// <returns>The lines with recorded authors, or <paramref name="lines"/> itself when no line is committed.</returns>
    /// <exception cref="GitCommandException">
    /// An id is not a full object name, the pass's output hit the output cap, or its records do not match the ids.
    /// </exception>
    private async Task<IReadOnlyList<BlameLine>> ReadRawAuthorsAsync(
        string root,
        IReadOnlyList<BlameLine> lines,
        CallContext ctx,
        CancellationToken cancellationToken)
    {
        var hashes = lines
            .Select(line => line.CommitHash)
            .Where(hash => hash != UncommittedCommitHash)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (hashes.Count == 0)
        {
            return lines;
        }

        if (!hashes.TrueForAll(RefVerifier.IsResolvedObjectName))
        {
            throw new GitCommandException("blame returned a malformed commit hash");
        }

        var intent = new GitIntent
        {
            Subcommand = "log",
            RepoRoot = root,
            Flags = ["--no-walk", "--stdin", .. BlameParser.RawAuthorFormatFlags],
            StandardInput = Encoding.ASCII.GetBytes(string.Join('\n', hashes) + "\n"),
        };
        var result = await common.ExecuteAsync(intent, ctx, cancellationToken: cancellationToken);
        if (result.Truncated)
        {
            throw new GitCommandException(AuthorPassCutMessage);
        }

        var rawAuthors = BlameParser.ParseRawAuthors(result.Stdout, hashes.ToHashSet(StringComparer.Ordinal));
        return BlameParser.WithRawAuthors(lines, rawAuthors);
    }
}