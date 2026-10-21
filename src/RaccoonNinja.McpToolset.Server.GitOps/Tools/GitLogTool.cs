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
public sealed class GitLogTool(ToolCommon common, IRefVerifier refVerifier)
{
    [McpServerTool(Name = "git_log")]
    [Description("Return a list of commits matching the given filters.")]
    public Task<ResultEnvelope> InvokeAsync(
        [Description("Absolute working directory.")] string cwd,
        [Description("Restrict to commits touching these repo-relative paths.")] string[] paths = null,
        [Description("Filter by author substring (regex per git --author).")] string author = null,
        [Description("Only commits more recent than this (git --since spec).")] string since = null,
        [Description("Only commits older than this (git --until spec).")] string until = null,
        [Description("Filter to commits whose message matches this pattern. A basic regular expression by default (git --grep), so '|' is a literal and '\\d' is a 'd'; set perlRegex for alternation and Perl escapes.")] string grep = null,
        [Description("Pickaxe search: when this string was added or removed (git -S).")] string pickaxe = null,
        [Description("Cap the number of commits returned. Default 50.")] int maxCount = 50,
        [Description("Search from this ref, or a range A..B / A...B (branch, tag, SHA, HEAD~n, ...).")] string @ref = null,
        [Description("Follow renames when scoping to a single path.")] bool follow = false,
        [Description("Match grep (and author) as Perl-compatible regular expressions (git -P), the dialect git_grep uses for fixedString: false: alternation 'a|b', '\\d', '\\s', '+', '?', and grouping work. Default false, which is git's basic regular expression where '|' is a literal character. A git built without PCRE reports PcreUnavailable.")] bool perlRegex = false,
        [Description("Walk every ref under refs/ plus HEAD (git --all): local branches, remote-tracking branches, tags, and the stash. Cannot be combined with ref.")] bool allRefs = false,
        CancellationToken cancellationToken = default)
    {
        var ctx = common.MakeContext("git_log");
        var holder = new RootHolder();
        return common.WrapAsync(ctx, holder, async () =>
        {
            var root = await common.ResolveAndLogAsync(cwd, ctx, holder, cancellationToken);
            if (allRefs && !string.IsNullOrWhiteSpace(@ref))
            {
                throw new RejectedArgumentException(
                    "allRefs cannot be combined with ref",
                    new Dictionary<string, object> { ["param"] = "all_refs" });
            }

            var verifiedRefs = await refVerifier.VerifyOptionalRefsOrRangesAsync(root, [@ref], cancellationToken);

            var intent = new GitIntent
            {
                Subcommand = "log",
                RepoRoot = root,
                Flags = BuildFlags(follow, paths, perlRegex, allRefs),
                AttachedOptions = BuildAttachedOptions(author, since, until, grep, pickaxe, maxCount),
                VerifiedRefs = verifiedRefs,
                Pathspecs = ToolCommon.ConfinePaths(root, paths),
            };
            var result = await common.ExecuteAsync(intent, ctx, stderrClassifier: GitGrepTool.ClassifyPcreStderr, cancellationToken: cancellationToken);
            var commits = LogParser.Parse(result.Stdout);

            var filtersApplied = FiltersAppliedBuilder.Create()
                .Redact("author", author)
                .Redact("since", since)
                .Redact("until", until)
                .Redact("grep", grep)
                .Redact("pickaxe", pickaxe)
                .Redact("ref", @ref)
                .Flag("perl_regex", perlRegex)
                .Flag("all_refs", allRefs)
                .Number("paths_count", paths?.Length ?? 0)
                .Number("max_count", maxCount)
                .Build();
            return ToolCommon.ListSuccess(commits, root, filtersApplied: filtersApplied, truncated: result.Truncated);
        });
    }

    /// <summary>
    /// Server-built log flags: <c>--follow</c> when scoping renames to a single path, exactly one
    /// dialect for the limiting patterns (<c>--grep</c>, <c>--author</c>), <c>--all</c> to walk every
    /// ref under <c>refs/</c> plus <c>HEAD</c>, then the pretty-format directive that
    /// <see cref="LogParser"/> expects. The dialect is <c>--perl-regexp</c> or <c>--basic-regexp</c>,
    /// always emitted: <c>git log</c> reads <c>grep.patternType</c> from the user's and the
    /// repository's config, and a command-line dialect overrides it.
    /// </summary>
    private static List<string> BuildFlags(bool follow, string[] paths, bool perlRegex, bool allRefs)
    {
        var flags = new List<string>();
        if (follow && paths is { Length: 1 }) flags.Add("--follow");
        flags.Add(perlRegex ? "--perl-regexp" : "--basic-regexp");
        if (allRefs) flags.Add("--all");
        flags.Add($"--pretty=format:{LogParser.LogPrettyFormat}");
        return flags;
    }

    /// <summary>Map each non-empty filter onto its attached-form option (<c>--author</c>, <c>--since</c>, … <c>--max-count</c>).</summary>
    private static List<AttachedOption> BuildAttachedOptions(
        string author, string since, string until, string grep, string pickaxe, int maxCount)
    {
        var attached = new List<AttachedOption>();
        if (!string.IsNullOrWhiteSpace(author)) attached.Add(new AttachedOption("--author", author));
        if (!string.IsNullOrWhiteSpace(since)) attached.Add(new AttachedOption("--since", since));
        if (!string.IsNullOrWhiteSpace(until)) attached.Add(new AttachedOption("--until", until));
        if (!string.IsNullOrWhiteSpace(grep)) attached.Add(new AttachedOption("--grep", grep));
        if (!string.IsNullOrWhiteSpace(pickaxe)) attached.Add(new AttachedOption("-S", pickaxe));
        if (maxCount > 0) attached.Add(new AttachedOption("--max-count", maxCount.ToString(CultureInfo.InvariantCulture)));
        return attached;
    }
}