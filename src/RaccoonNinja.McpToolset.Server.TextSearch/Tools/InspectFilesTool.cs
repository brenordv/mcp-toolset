using System.ComponentModel;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Files.Text;
using RaccoonNinja.McpToolset.Server.TextSearch.Configuration;
using RaccoonNinja.McpToolset.Server.TextSearch.Content;
using RaccoonNinja.McpToolset.Server.TextSearch.Envelope;
using RaccoonNinja.McpToolset.Server.TextSearch.Models;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tools;

/// <summary>The <c>inspect_files</c> tool: report the encoding and text shape of the selected files.</summary>
[McpServerToolType]
public sealed class InspectFilesTool(ToolCommon common, SearchConfig config, RootRegistry registry, IEncodingDetector detector)
{
    /// <summary>Report per-file encoding, BOM, line endings, final newline, and counts across the targeted roots.</summary>
    /// <param name="glob">A glob over the root-relative path (primary).</param>
    /// <param name="regex">A regex over the root-relative path (escape hatch).</param>
    /// <param name="paths">An explicit list of root-relative paths.</param>
    /// <param name="root">The target root: a name, <c>@packages</c>, <c>@all</c>, or omitted (all workspace roots).</param>
    /// <param name="extensions">File extensions to keep (dot optional).</param>
    /// <param name="include_ignored">Whether to include ignored files.</param>
    /// <param name="case_sensitive">Whether matching is case-sensitive.</param>
    /// <param name="max_files">The page size, clamped to the ceiling.</param>
    /// <param name="cursor">A pagination cursor from a previous call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An envelope of per-file inspections with pagination metadata.</returns>
    [McpServerTool(Name = "inspect_files", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Report the text shape of the selected files: detected encoding and confidence, BOM, line endings "
        + "(lf/crlf/cr/mixed), whether the file ends with a newline, trailing-whitespace line count, line "
        + "count, size, and whether it is binary. Selector and root targeting are the same as find_files. "
        + "Files too large to read are skipped. Each result carries its root name.")]
    public Task<ResultEnvelope> InvokeAsync(
        [Description("A glob over the root-relative path, e.g. \"src/**/*.cs\". Exactly one of glob/regex/paths.")]
        string glob = null,
        [Description("A regex over the root-relative path. Exactly one of glob/regex/paths.")]
        string regex = null,
        [Description("Explicit root-relative paths to inspect. Exactly one of glob/regex/paths.")]
        string[] paths = null,
        [Description("Target root: a name, \"@packages\", \"@all\", or omitted for all workspace roots.")]
        string root = null,
        [Description("File extensions to keep (dot optional, case-insensitive). ANDed with the selector.")]
        string[] extensions = null,
        [Description("Include files matched by ignore rules. Default false; never bypasses the secret denylist.")]
        bool include_ignored = false,
        [Description("Match case-sensitively. Default false.")]
        bool case_sensitive = false,
        [Description("Maximum files to inspect in this page; clamped to the server ceiling.")]
        int max_files = 0,
        [Description("Opaque pagination cursor from a previous response; omit for the first page.")]
        string cursor = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = common.MakeContext("inspect_files");
        return common.WrapAsync(ctx, () =>
        {
            var targets = registry.Resolve(root);
            var selector = SelectorSupport.Build(config, glob, regex, paths, extensions, include_ignored, case_sensitive);
            SelectorSupport.EnsureNarrowedForPackages(targets, selector);
            if (SelectorSupport.TargetsPackage(targets))
            {
                common.PackageTargeted(ctx);
            }

            using var budget = SelectorSupport.CreateBudget(config, cancellationToken);
            var (files, windowTruncated, skippedSymlinks) = FileListing.Walk(targets, selector, config, budget.Token);
            var pageSize = SelectorSupport.PageSize(config, max_files);
            var page = FileListing.Paginate(files, windowTruncated, skippedSymlinks, config, root, cursor, pageSize);

            var results = new List<object>();
            foreach (var file in page.Items)
            {
                SelectorSupport.CheckBudget(config, budget.Token);
                var inspection = Inspect(ctx, targets[file.RootIndex].Reader, file);
                if (inspection is not null)
                {
                    results.Add(inspection);
                }
            }

            var filters = SelectorSupport
                .Filters(root, glob, regex, paths, extensions, include_ignored, case_sensitive)
                .Number("max_files", pageSize)
                .Build();

            return Task.FromResult(ToolCommon.ListSuccess(
                results,
                truncated: page.Truncated,
                cursor: page.Cursor,
                filtersApplied: filters,
                skippedSymlinks: page.SkippedSymlinks));
        });
    }

    private FileInspection Inspect(CallContext ctx, GatedFileReader reader, FlatFile file)
    {
        var read = reader.Read(file.Entry.RelativePath);
        if (!read.IsOk)
        {
            common.Refusal(ctx, RefusalReason.From(read.Status));
            return null;
        }

        var document = TextDocument.Load(read.Bytes, detector);
        return new FileInspection
        {
            Root = file.RootName,
            Path = file.Entry.RelativePath,
            Encoding = document.Encoding.Name,
            EncodingConfidence = document.Encoding.Confidence,
            HasBom = document.Encoding.HasBom,
            LineEndings = document.LineEndings.ToWire(),
            FinalNewline = document.FinalNewline,
            TrailingWhitespaceLines = document.TrailingWhitespaceLines,
            LineCount = document.LineCount,
            Size = file.Entry.Size,
            IsBinary = document.IsBinary,
        };
    }
}