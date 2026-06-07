using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.GitOps.Envelope;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Extensions;
using RaccoonNinja.McpToolset.Server.GitOps.Parsers;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Security;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tools;

[McpServerToolType]
public sealed class GitBlameTool(ToolCommon common, IRefVerifier refVerifier)
{
    [McpServerTool(Name = "git_blame")]
    [Description("Return per-line author/commit/timestamp for a file, optionally restricted to a line range.")]
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
            var root = await common.ResolveAndLogAsync(cwd, ctx, holder, cancellationToken).ConfigureAwait(false);
            var rel = PathConfinement.Confine(root, path ?? string.Empty);
            var verifiedRefs = await refVerifier.VerifyOptionalRefsAsync(root, [@ref], cancellationToken).ConfigureAwait(false);

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
                Pathspecs = [rel],
            };
            var result = await common.ExecuteAsync(intent, ctx, cancellationToken: cancellationToken).ConfigureAwait(false);
            var lines = BlameParser.Parse(result.Stdout);

            var filtersApplied = FiltersAppliedBuilder.Create()
                .Redact("path", path)
                .Redact("ref", @ref)
                .Optional("line_range", lineRange.HasValue ? new[] { lineRange.Value.Start, lineRange.Value.End } : null)
                .Build();
            return ToolCommon.ListSuccess(lines, root, filtersApplied: filtersApplied, truncated: result.Truncated);
        });
    }
}