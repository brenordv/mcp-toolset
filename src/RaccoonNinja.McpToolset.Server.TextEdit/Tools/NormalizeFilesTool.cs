using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.TextEdit.Configuration;
using RaccoonNinja.McpToolset.Server.TextEdit.Content;
using RaccoonNinja.McpToolset.Server.TextEdit.Envelope;
using RaccoonNinja.McpToolset.Server.TextEdit.Errors;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Tools;

/// <summary>The <c>normalize_files</c> tool: fix trailing whitespace, line endings, the final newline, and the byte-order mark across selected files.</summary>
[McpServerToolType]
public sealed class NormalizeFilesTool(ToolCommon common, EditConfig config, RootRegistry registry, GatedFileWriter writer)
{
    /// <summary>Normalize the selected files according to the options, journaling every file that changes.</summary>
    /// <param name="root">A root-relative subdirectory to scope to, or null for the whole root.</param>
    /// <param name="glob">A glob selecting files, or null.</param>
    /// <param name="regex">A regex over root-relative paths, or null.</param>
    /// <param name="paths">Explicit root-relative file paths, or null.</param>
    /// <param name="extensions">An extension filter (no leading dot), or null.</param>
    /// <param name="case_sensitive">Whether glob/regex matching is case-sensitive.</param>
    /// <param name="max_files">The cap on files acted on; zero uses the default.</param>
    /// <param name="trim_trailing_whitespace">Whether to strip trailing spaces and tabs from each line.</param>
    /// <param name="line_endings">Line endings: <c>preserve</c>, <c>lf</c>, or <c>crlf</c>.</param>
    /// <param name="final_newline">Final newline: <c>preserve</c>, <c>ensure</c>, or <c>trim</c>.</param>
    /// <param name="bom">Byte-order mark: <c>preserve</c> or <c>strip</c>.</param>
    /// <param name="source_encoding">An explicit source encoding, or null to auto-detect.</param>
    /// <param name="dry_run">Whether to preview without writing.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A single-item envelope carrying the mutation result.</returns>
    [McpServerTool(Name = "normalize_files", Destructive = false, Idempotent = true)]
    [Description(
        "Normalize whitespace and encoding across selected files: strip trailing whitespace, rewrite line "
        + "endings (preserve/lf/crlf), fix the final newline (preserve/ensure/trim), and strip a byte-order "
        + "mark. Select with exactly one of glob, regex, or paths (or none for the whole root), optionally "
        + "narrowed by extensions. Every changed file is journaled for undo. Pass dry_run to preview.")]
    public Task<ResultEnvelope> InvokeAsync(
        [Description("A root-relative subdirectory to scope to; omit for the whole root.")] string root = null,
        [Description("A glob selecting files; with no '/' it matches the basename at any depth.")] string glob = null,
        [Description("A regex over root-relative paths selecting files.")] string regex = null,
        [Description("Explicit root-relative file paths to act on.")] string[] paths = null,
        [Description("Restrict to these file extensions (no leading dot).")] string[] extensions = null,
        [Description("Match glob/regex case-sensitively (default false).")] bool case_sensitive = false,
        [Description("Cap the number of files acted on (0 uses the server default).")] int max_files = 0,
        [Description("Strip trailing spaces and tabs from each line.")] bool trim_trailing_whitespace = false,
        [Description("Line endings: preserve (default), lf, or crlf.")] string line_endings = null,
        [Description("Final newline: preserve (default), ensure, or trim.")] string final_newline = null,
        [Description("Byte-order mark: preserve (default) or strip.")] string bom = null,
        [Description("Decode with this explicit encoding instead of auto-detecting (bypasses the confidence gate).")] string source_encoding = null,
        [Description("Preview the changes and their diffs without writing.")] bool dry_run = false,
        CancellationToken cancellationToken = default)
    {
        var ctx = common.MakeContext("normalize_files");
        return common.WrapAsync(ctx, () =>
        {
            var options = ParseOptions(trim_trailing_whitespace, line_endings, final_newline, bom);
            var normalizer = new Normalizer(options);

            var effectiveMax = SelectorSupport.PageSize(config, max_files);
            var selector = SelectorSupport.Build(root, glob, regex, paths, extensions, case_sensitive, effectiveMax);

            using var budget = SelectorSupport.CreateBudget(config, cancellationToken);
            var walk = SelectorSupport.Run(registry.Selection, selector, config, budget.Token);
            var relativePaths = walk.Entries.Select(entry => entry.RelativePath).ToList();

            var argsSummary = string.Create(
                CultureInfo.InvariantCulture,
                $"normalize trim={options.TrimTrailingWhitespace} le={options.LineEndings} fnl={options.FinalNewline} bom={options.Bom}");

            var outcome = writer.Apply(
                "normalize_files",
                relativePaths,
                normalizer,
                argsSummary,
                expectedMatchCount: null,
                dry_run,
                source_encoding,
                walk.SkippedSymlinks,
                walk.Truncated,
                budget.Token);

            common.BatchCommitted(ctx, outcome.BatchId, outcome.Attempted, outcome.Changed, outcome.Refused, dry_run);

            var filters = SelectorSupport
                .Filters(root, glob, regex, paths, extensions, case_sensitive, dry_run)
                .Build();
            return Task.FromResult(ToolCommon.SingleSuccess(ResultMapping.ToMutationResult(outcome, dry_run), filters));
        });
    }

    private static NormalizeOptions ParseOptions(bool trim, string lineEndings, string finalNewline, string bom)
        => new()
        {
            TrimTrailingWhitespace = trim,
            LineEndings = ParseLineEndings(lineEndings),
            FinalNewline = ParseFinalNewline(finalNewline),
            Bom = ParseBom(bom),
        };

    private static LineEndingMode ParseLineEndings(string value)
        => Normalize(value) switch
        {
            "" or "preserve" => LineEndingMode.Preserve,
            "lf" => LineEndingMode.Lf,
            "crlf" => LineEndingMode.Crlf,
            _ => throw TextEditException.InvalidArgument("line_endings must be one of preserve, lf, crlf"),
        };

    private static FinalNewlineMode ParseFinalNewline(string value)
        => Normalize(value) switch
        {
            "" or "preserve" => FinalNewlineMode.Preserve,
            "ensure" => FinalNewlineMode.Ensure,
            "trim" => FinalNewlineMode.Trim,
            _ => throw TextEditException.InvalidArgument("final_newline must be one of preserve, ensure, trim"),
        };

    private static BomMode ParseBom(string value)
        => Normalize(value) switch
        {
            "" or "preserve" => BomMode.Preserve,
            "strip" => BomMode.Strip,
            _ => throw TextEditException.InvalidArgument("bom must be one of preserve, strip"),
        };

    private static string Normalize(string value)
        => value?.Trim().ToLowerInvariant() ?? string.Empty;
}