using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.GitOps.Envelope;
using RaccoonNinja.McpToolset.Server.GitOps.Extensions;
using RaccoonNinja.McpToolset.Server.GitOps.Models;
using RaccoonNinja.McpToolset.Server.GitOps.Parsers;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Security;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tools;

[McpServerToolType]
public sealed class GitDiffTool(ToolCommon common, IRefVerifier refVerifier)
{
    [McpServerTool(Name = "git_diff")]
    [Description("Return a unified-diff result. Modes: staged | ref_to_ref | worktree | stat_only.")]
    public Task<ResultEnvelope> InvokeAsync(
        [Description("Absolute working directory.")] string cwd,
        [Description("If true, diff the index against HEAD (cached).")] bool staged = false,
        [Description("Diff starting ref (branch/tag/SHA).")] string fromRef = null,
        [Description("Diff ending ref (defaults to working tree when omitted with from_ref).")] string toRef = null,
        [Description("Restrict to these repo-relative paths.")] string[] paths = null,
        [Description("Override the unified-diff context line count (git -U).")] int? contextLines = null,
        [Description("Return only per-file numstat (no patch).")] bool statOnly = false,
        CancellationToken cancellationToken = default)
    {
        var ctx = common.MakeContext("git_diff");
        var holder = new RootHolder();
        return common.WrapAsync(ctx, holder, async () =>
        {
            var root = await common.ResolveAndLogAsync(cwd, ctx, holder, cancellationToken).ConfigureAwait(false);
            var verifiedRefs = await refVerifier.VerifyOptionalRefsAsync(root, [fromRef, toRef], cancellationToken).ConfigureAwait(false);

            var flags = BuildFlags(staged);
            var attached = BuildAttachedOptions(contextLines);
            var pathspecs = ToolCommon.ConfinePaths(root, paths);
            var mode = DetermineMode(staged, verifiedRefs.Count > 0, statOnly);

            var (resultModel, truncated) = statOnly
                ? await ExecuteStatOnlyAsync(root, flags, verifiedRefs, pathspecs, ctx, cancellationToken).ConfigureAwait(false)
                : await ExecuteUnifiedAsync(root, flags, attached, verifiedRefs, pathspecs, mode, ctx, cancellationToken).ConfigureAwait(false);

            var filtersApplied = FiltersAppliedBuilder.Create()
                .Flag("staged", staged)
                .Redact("from_ref", fromRef)
                .Redact("to_ref", toRef)
                .Number("paths_count", paths?.Length ?? 0)
                .Optional("context_lines", contextLines)
                .Flag("stat_only", statOnly)
                .Build();
            return ToolCommon.SingleSuccess(resultModel, root, filtersApplied: filtersApplied, truncated: truncated);
        });
    }

    /// <summary>Server-built diff flags: <c>--cached</c> when diffing the staged index.</summary>
    private static List<string> BuildFlags(bool staged)
    {
        var flags = new List<string>();
        if (staged) flags.Add("--cached");
        return flags;
    }

    /// <summary>Translate a non-negative <paramref name="contextLines"/> override into a <c>-U&lt;n&gt;</c> attached option.</summary>
    private static List<AttachedOption> BuildAttachedOptions(int? contextLines)
    {
        var attached = new List<AttachedOption>();
        if (contextLines is { } ctxVal && ctxVal >= 0)
            attached.Add(new AttachedOption("-U", ctxVal.ToString(CultureInfo.InvariantCulture)));
        return attached;
    }

    /// <summary>Pick the reported mode by precedence: stat-only &gt; ref-to-ref &gt; staged &gt; worktree.</summary>
    private static string DetermineMode(bool staged, bool hasRefs, bool statOnly)
    {
        var mode = "worktree";
        if (staged) mode = "staged";
        if (hasRefs) mode = "ref_to_ref";
        if (statOnly) mode = "stat_only";
        return mode;
    }

    /// <summary>
    /// Run the <c>--numstat -z</c> (counts) and <c>--name-status -z</c> (change kinds) passes, then
    /// assemble a counts-only result (no patch text) with the change types overlaid by path.
    /// </summary>
    private async Task<(DiffResult Model, bool Truncated)> ExecuteStatOnlyAsync(
        string root, List<string> flags, List<string> verifiedRefs, List<string> pathspecs,
        CallContext ctx, CancellationToken cancellationToken)
    {
        var numstatResult = await common.ExecuteAsync(
            NumstatIntent(root, flags, verifiedRefs, pathspecs), ctx, cancellationToken: cancellationToken).ConfigureAwait(false);
        var nameStatusResult = await common.ExecuteAsync(
            NameStatusIntent(root, flags, verifiedRefs, pathspecs), ctx, cancellationToken: cancellationToken).ConfigureAwait(false);

        var numstat = DiffParser.ParseNumstatZ(numstatResult.Stdout);
        var changeTypes = DiffParser.ParseNameStatusZ(nameStatusResult.Stdout);
        var files = BuildStatOnlyFiles(numstat);
        var truncated = numstatResult.Truncated || nameStatusResult.Truncated;
        var model = DiffParser.AssembleDiffResult(files, "stat_only", truncated, changeTypes: changeTypes);
        return (model, truncated);
    }

    /// <summary>Run the patch and <c>--numstat</c> passes, then merge them into a unified diff result.</summary>
    private async Task<(DiffResult Model, bool Truncated)> ExecuteUnifiedAsync(
        string root, List<string> flags, List<AttachedOption> attached,
        List<string> verifiedRefs, List<string> pathspecs, string mode,
        CallContext ctx, CancellationToken cancellationToken)
    {
        var unifiedIntent = new GitIntent
        {
            Subcommand = "diff",
            RepoRoot = root,
            Flags = flags,
            AttachedOptions = attached,
            VerifiedRefs = verifiedRefs,
            Pathspecs = pathspecs,
        };
        var unifiedResult = await common.ExecuteAsync(unifiedIntent, ctx, cancellationToken: cancellationToken).ConfigureAwait(false);
        var numstatResult = await common.ExecuteAsync(
            NumstatIntent(root, flags, verifiedRefs, pathspecs), ctx, cancellationToken: cancellationToken).ConfigureAwait(false);

        var files = DiffParser.ParseUnified(unifiedResult.Stdout);
        var numstat = DiffParser.ParseNumstatZ(numstatResult.Stdout);
        var truncated = unifiedResult.Truncated || numstatResult.Truncated;
        var model = DiffParser.AssembleDiffResult(files, mode, truncated, numstat);
        return (model, truncated);
    }

    /// <summary>Build the companion <c>git diff --numstat -z</c> intent that backs every diff mode.</summary>
    private static GitIntent NumstatIntent(string root, List<string> flags, List<string> verifiedRefs, List<string> pathspecs)
        => new()
        {
            Subcommand = "diff",
            RepoRoot = root,
            Flags = [.. flags, "--numstat", "-z"],
            VerifiedRefs = verifiedRefs,
            Pathspecs = pathspecs,
        };

    /// <summary>Build the companion <c>git diff --name-status -z</c> intent that supplies change kinds for stat-only mode.</summary>
    private static GitIntent NameStatusIntent(string root, List<string> flags, List<string> verifiedRefs, List<string> pathspecs)
        => new()
        {
            Subcommand = "diff",
            RepoRoot = root,
            Flags = [.. flags, "--name-status", "-z"],
            VerifiedRefs = verifiedRefs,
            Pathspecs = pathspecs,
        };

    /// <summary>
    /// Project numstat entries into <see cref="FileDiff"/> records. The change kind is unknown from
    /// counts alone; it is overlaid afterwards from the <c>--name-status</c> companion pass.
    /// </summary>
    private static List<FileDiff> BuildStatOnlyFiles(IReadOnlyDictionary<string, (int Additions, int Deletions)> numstat)
    {
        var files = new List<FileDiff>(numstat.Count);
        files.AddRange(numstat.Select(entry => new FileDiff
        {
            Path = entry.Key,
            // --numstat reports counts only; the change kind is not derivable here.
            ChangeType = ChangeType.Unknown,
            Additions = entry.Value.Additions,
            Deletions = entry.Value.Deletions,
        }));
        return files;
    }
}