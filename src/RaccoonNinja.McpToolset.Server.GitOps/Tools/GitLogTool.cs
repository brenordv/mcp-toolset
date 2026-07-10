using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.GitOps.Envelope;
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
        [Description("Substring match against commit message (git --grep).")] string grep = null,
        [Description("Pickaxe search: when this string was added or removed (git -S).")] string pickaxe = null,
        [Description("Cap the number of commits returned. Default 50.")] int maxCount = 50,
        [Description("Search from this ref (branch, tag, SHA, HEAD~n, ...).")] string @ref = null,
        [Description("Follow renames when scoping to a single path.")] bool follow = false,
        CancellationToken cancellationToken = default)
    {
        var ctx = common.MakeContext("git_log");
        var holder = new RootHolder();
        return common.WrapAsync(ctx, holder, async () =>
        {
            var root = await common.ResolveAndLogAsync(cwd, ctx, holder, cancellationToken).ConfigureAwait(false);
            var verifiedRefs = await refVerifier.VerifyOptionalRefsAsync(root, [@ref], cancellationToken).ConfigureAwait(false);

            var intent = new GitIntent
            {
                Subcommand = "log",
                RepoRoot = root,
                Flags = BuildFlags(follow, paths),
                AttachedOptions = BuildAttachedOptions(author, since, until, grep, pickaxe, maxCount),
                VerifiedRefs = verifiedRefs,
                Pathspecs = ToolCommon.ConfinePaths(root, paths),
            };
            var result = await common.ExecuteAsync(intent, ctx, cancellationToken: cancellationToken).ConfigureAwait(false);
            var commits = LogParser.Parse(result.Stdout);

            var filtersApplied = FiltersAppliedBuilder.Create()
                .Redact("author", author)
                .Redact("since", since)
                .Redact("until", until)
                .Redact("grep", grep)
                .Redact("pickaxe", pickaxe)
                .Redact("ref", @ref)
                .Number("paths_count", paths?.Length ?? 0)
                .Number("max_count", maxCount)
                .Build();
            return ToolCommon.ListSuccess(commits, root, filtersApplied: filtersApplied, truncated: result.Truncated);
        });
    }

    /// <summary>
    /// Server-built log flags: <c>--follow</c> when scoping renames to a single path, then the
    /// pretty-format directive that <see cref="LogParser"/> expects.
    /// </summary>
    private static List<string> BuildFlags(bool follow, string[] paths)
    {
        var flags = new List<string>();
        if (follow && paths is { Length: 1 }) flags.Add("--follow");
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