using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Files.Selection;
using RaccoonNinja.McpToolset.Server.TextEdit.Configuration;
using RaccoonNinja.McpToolset.Server.TextEdit.Content;
using RaccoonNinja.McpToolset.Server.TextEdit.Envelope;
using RaccoonNinja.McpToolset.Server.TextEdit.Errors;
using RaccoonNinja.McpToolset.Server.TextEdit.Logging;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Tools;

/// <summary>The <c>replace_text</c> tool: substitute a literal or regex pattern across selected files, with regex back-references and an optional match-count guard.</summary>
[McpServerToolType]
public sealed class ReplaceTextTool(ToolCommon common, EditConfig config, ScopeResolver resolver, GatedFileWriter writer)
{
    /// <summary>Replace <paramref name="pattern"/> with <paramref name="replacement"/> across the selected files, journaling every file that changes.</summary>
    /// <param name="pattern">The literal string or regex to match.</param>
    /// <param name="replacement">The replacement (regex back-references <c>$1</c>/<c>${name}</c>/<c>$$</c> apply when <paramref name="is_regex"/>).</param>
    /// <param name="cwd">An absolute working directory inside the base root to scope to; omit for the whole base root.</param>
    /// <param name="glob">A glob selecting files, or null.</param>
    /// <param name="regex">A regex over scope-relative paths selecting files, or null.</param>
    /// <param name="paths">Explicit cwd-relative file paths, or null.</param>
    /// <param name="extensions">An extension filter (no leading dot), or null.</param>
    /// <param name="is_regex">Whether <paramref name="pattern"/> is a regex.</param>
    /// <param name="case_sensitive">Whether matching is case-sensitive.</param>
    /// <param name="expected_match_count">When set, abort before any write unless exactly this many matches would be rewritten.</param>
    /// <param name="source_encoding">An explicit source encoding, or null to auto-detect.</param>
    /// <param name="max_files">The cap on files acted on; zero uses the default.</param>
    /// <param name="dry_run">Whether to preview without writing.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A single-item envelope carrying the mutation result.</returns>
    [McpServerTool(Name = "replace_text", Destructive = true, Idempotent = false)]
    [Description(
        "Replace a literal or regex pattern across selected files. With is_regex, the replacement uses .NET "
        + "back-references ($1, ${name}, $$ for a literal $). Pass cwd (an absolute working directory inside "
        + "the base root) to scope the call to one project, or omit it to edit across the whole base root. "
        + "Select with exactly one of glob, regex, or paths (or none for the whole scope), optionally narrowed "
        + "by extensions; explicit paths are relative to cwd and are confined to it. Reported and undoable "
        + "paths are relative to the base root. Set expected_match_count to abort before any write unless "
        + "exactly that many matches would change. Pass dry_run to preview a diff. Every changed file is "
        + "journaled for undo.")]
    public Task<ResultEnvelope> InvokeAsync(
        [Description("The literal string or regex to match.")] string pattern,
        [Description("The replacement text; with is_regex, $1/${name} are back-references and $$ is a literal $.")] string replacement,
        [Description("Absolute working directory inside the base root to scope this call to. Omit to edit across the whole base root.")] string cwd = null,
        [Description("A glob selecting files; with no '/' it matches the basename at any depth.")] string glob = null,
        [Description("A regex over scope-relative paths selecting files.")] string regex = null,
        [Description("Explicit file paths to act on, relative to cwd (or the base root when cwd is omitted).")] string[] paths = null,
        [Description("Restrict to these file extensions (no leading dot).")] string[] extensions = null,
        [Description("Treat the pattern as a regex (default false, literal).")] bool is_regex = false,
        [Description("Match case-sensitively (default false).")] bool case_sensitive = false,
        [Description("Abort before any write unless exactly this many matches would change.")] int? expected_match_count = null,
        [Description("Decode with this explicit encoding instead of auto-detecting (bypasses the confidence gate).")] string source_encoding = null,
        [Description("Cap the number of files acted on (0 uses the server default).")] int max_files = 0,
        [Description("Preview the changes and their diffs without writing.")] bool dry_run = false,
        CancellationToken cancellationToken = default)
    {
        var ctx = common.MakeContext("replace_text");
        return common.WrapAsync(ctx, () =>
        {
            var scope = resolver.Resolve(cwd);
            if (string.IsNullOrWhiteSpace(cwd))
            {
                common.WholeBase(ctx);
            }

            var replacer = BuildReplacer(pattern, replacement, is_regex, case_sensitive);
            if (!replacer.IsNonBacktracking)
            {
                common.RegexFallback(ctx);
            }

            var effectiveMax = SelectorSupport.PageSize(config, max_files);
            var selector = SelectorSupport.Build(glob, regex, paths, extensions, case_sensitive, effectiveMax);

            using var budget = SelectorSupport.CreateBudget(config, cancellationToken);
            var walk = SelectorSupport.Run(scope.Selection, selector, config, budget.Token);
            var relativePaths = walk.Entries.Select(entry => entry.RelativePath).ToList();

            var argsSummary = string.Create(
                CultureInfo.InvariantCulture,
                $"replace regex={is_regex} plen={pattern.Length} phash={LogScrubbing.HashedValue(pattern)} rlen={replacement.Length} rhash={LogScrubbing.HashedValue(replacement)}");

            var outcome = writer.Apply(
                "replace_text",
                relativePaths,
                replacer,
                argsSummary,
                expected_match_count,
                dry_run,
                source_encoding,
                walk.SkippedSymlinks,
                walk.Truncated,
                scope.Effective,
                budget.Token);

            common.BatchCommitted(ctx, outcome.BatchId, outcome.Attempted, outcome.Changed, outcome.Refused, dry_run);

            var filters = SelectorSupport
                .Filters(scope.ScopeKey, glob, regex, paths, extensions, case_sensitive, dry_run)
                .Build();
            return Task.FromResult(ToolCommon.SingleSuccess(ResultMapping.ToMutationResult(outcome, dry_run), filters));
        });
    }

    private Replacer BuildReplacer(string pattern, string replacement, bool isRegex, bool caseSensitive)
    {
        try
        {
            return new Replacer(pattern, replacement, isRegex, caseSensitive, config);
        }
        catch (ArgumentException)
        {
            throw TextEditException.InvalidArgument("pattern must not be empty");
        }
        catch (RegexCompilationException ex)
        {
            throw TextEditException.PatternInvalid(ex.Message);
        }
    }
}