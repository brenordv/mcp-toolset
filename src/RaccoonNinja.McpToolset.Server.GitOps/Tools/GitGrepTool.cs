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
public sealed class GitGrepTool(ToolCommon common, IRefVerifier refVerifier)
{
    private static readonly HashSet<int> GrepAllowedExitCodes = [0, 1];

    [McpServerTool(Name = "git_grep")]
    [Description("Search tracked content. Set 'ref' to grep historical revisions without checking them out.")]
    public Task<ResultEnvelope> InvokeAsync(
        [Description("Absolute working directory.")] string cwd,
        [Description("Pattern to search for.")] string pattern = null,
        [Description("Restrict to these repo-relative paths.")] string[] paths = null,
        [Description("Optional ref to grep at, instead of HEAD/working tree.")] string @ref = null,
        [Description("Case-insensitive search.")] bool ignoreCase = false,
        [Description("Treat pattern as a fixed string (default). Set false to use regex.")] bool fixedString = true,
        [Description("Cap matches per file (git grep -m).")] int? maxCount = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = common.MakeContext("git_grep");
        var holder = new RootHolder();
        return common.WrapAsync(ctx, holder, async () =>
        {
            var root = await common.ResolveAndLogAsync(cwd, ctx, holder, cancellationToken).ConfigureAwait(false);
            var verifiedRefs = await refVerifier.VerifyOptionalRefsAsync(root, [@ref], cancellationToken).ConfigureAwait(false);

            var intent = new GitIntent
            {
                Subcommand = "grep",
                RepoRoot = root,
                Flags = BuildFlags(ignoreCase, fixedString),
                AttachedOptions = BuildAttachedOptions(pattern, maxCount),
                VerifiedRefs = verifiedRefs,
                Pathspecs = ToolCommon.ConfinePaths(root, paths),
            };
            // Exit 1 means "no matches", a success for grep, so it is allowed; any other
            // non-zero exit is surfaced as a command error by ExecuteAsync. A git built without
            // PCRE2 exits non-zero on -P rather than matching nothing, so the classifier maps that
            // case to a loud PcreUnavailable error instead of an opaque command error.
            var result = await common.ExecuteAsync(
                intent,
                ctx,
                allowedExitCodes: GrepAllowedExitCodes,
                stderrClassifier: ClassifyPcreStderr,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var matches = GrepParser.Parse(result.Stdout, @ref);
            var filtersApplied = FiltersAppliedBuilder.Create()
                .Redact("pattern", pattern)
                .Redact("ref", @ref)
                .Flag("ignore_case", ignoreCase)
                .Flag("fixed_string", fixedString)
                .Optional("regex_engine", fixedString ? "fixed" : "perl")
                .Number("paths_count", paths?.Length ?? 0)
                .Build();
            return ToolCommon.ListSuccess(matches, root, preFilterCount: matches.Count, filtersApplied: filtersApplied, truncated: result.Truncated);
        });
    }

    /// <summary>Build the attached-form options: the search pattern (<c>-e</c>) plus an optional per-file cap (<c>-m</c>).</summary>
    private static List<AttachedOption> BuildAttachedOptions(string pattern, int? maxCount)
    {
        var attached = new List<AttachedOption> { new("-e", pattern ?? string.Empty) };
        if (maxCount.HasValue)
            attached.Add(new AttachedOption("-m", maxCount.Value.ToString(CultureInfo.InvariantCulture)));
        return attached;
    }

    /// <summary>
    /// Server-built grep flags: always <c>-n -z</c>, an optional <c>-i</c>, and exactly one matcher
    /// <c>-F</c> (fixed string) or <c>-P</c> (Perl-compatible regex). <c>-P</c> is used for regex search
    /// because callers expect <c>\s</c>, <c>\w</c>, <c>\d</c> and bare quantifiers; git's default BRE
    /// silently ignores them. The two matchers are mutually exclusive, so only one is ever emitted.
    /// </summary>
    /// <param name="ignoreCase">Whether to add <c>-i</c> for case-insensitive matching.</param>
    /// <param name="fixedString">When true, emit <c>-F</c>; otherwise emit <c>-P</c>.</param>
    /// <returns>The server-controlled flag list.</returns>
    private static List<string> BuildFlags(bool ignoreCase, bool fixedString)
    {
        var flags = new List<string> { "-n", "-z" };
        if (ignoreCase)
        {
            flags.Add("-i");
        }

        flags.Add(fixedString ? "-F" : "-P");
        return flags;
    }

    /// <summary>
    /// Recognize the git stderr emitted when <c>-P</c> is requested on a git built without PCRE2
    /// (<c>USE_LIBPCRE</c>) and map it to a <see cref="PcreUnavailableException"/>; returns
    /// <c>null</c> for any other stderr so the caller falls back to the generic command error.
    /// Detection is required because such a build exits non-zero with this fixed, user-data-free
    /// message rather than matching nothing, which would otherwise read as a silent "no results".
    /// </summary>
    /// <param name="stderr">The decoded git stderr from the failed grep.</param>
    /// <returns>A <see cref="PcreUnavailableException"/> when the signature matches; otherwise <c>null</c>.</returns>
    public static GitCheckException ClassifyPcreStderr(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return null;
        }

        var lacksPcre = stderr.Contains("USE_LIBPCRE", StringComparison.Ordinal)
            || stderr.Contains("Perl-compatible regex", StringComparison.OrdinalIgnoreCase);
        return lacksPcre ? new PcreUnavailableException() : null;
    }
}