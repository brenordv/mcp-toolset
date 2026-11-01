using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Common.Files.Selection;
using RaccoonNinja.McpToolset.Common.Files.Text;
using RaccoonNinja.McpToolset.Server.TextSearch.Configuration;
using RaccoonNinja.McpToolset.Server.TextSearch.Content;
using RaccoonNinja.McpToolset.Server.TextSearch.Envelope;
using RaccoonNinja.McpToolset.Server.TextSearch.Errors;
using RaccoonNinja.McpToolset.Server.TextSearch.Logging;
using RaccoonNinja.McpToolset.Server.TextSearch.Models;
using RaccoonNinja.McpToolset.Server.TextSearch.Paging;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tools;

/// <summary>The <c>search_text</c> tool: line-oriented literal or regex search across the call's scope.</summary>
[McpServerToolType]
public sealed class SearchTextTool(ToolCommon common, SearchConfig config, ScopeResolver resolver, IEncodingDetector detector)
{
    /// <summary>
    /// Search file contents for a literal or regex pattern, line by line, across the call's scope. In count mode
    /// the result is one matching-line count per file, with the total across every page on the envelope's
    /// <c>total_match_count</c>; <c>count_only</c> must stay the same across pages.
    /// </summary>
    /// <param name="pattern">The content pattern to find.</param>
    /// <param name="is_regex">Whether <paramref name="pattern"/> is a regex.</param>
    /// <param name="glob">A glob selecting which files to search.</param>
    /// <param name="regex">A regex selecting which files to search (over the path, not the content).</param>
    /// <param name="paths">Explicit files to search.</param>
    /// <param name="cwd">An absolute working directory inside the base root to scope the call to; omit for the whole base root.</param>
    /// <param name="extensions">File extensions to keep (dot optional).</param>
    /// <param name="include_ignored">Globs that re-include otherwise-ignored paths (built-in default tier only, e.g. a tracked build directory); never re-includes a project ignore-file path, and never bypasses the secret denylist.</param>
    /// <param name="case_sensitive">Whether both file matching and content matching are case-sensitive.</param>
    /// <param name="context_lines">Lines of context around each match; clamped to the ceiling.</param>
    /// <param name="max_matches_per_file">The per-file match cap; clamped to the ceiling.</param>
    /// <param name="max_results">The page size (matches, or files when files_only or count_only); clamped to the ceiling.</param>
    /// <param name="files_only">Return one entry per matching file instead of each match.</param>
    /// <param name="count_only">Return one matching-line count per matching file instead of each match.</param>
    /// <param name="cursor">A pagination cursor from a previous call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An envelope of matches (or matching files, or per-file counts) with pagination metadata.</returns>
    [McpServerTool(Name = "search_text", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Search file contents for a pattern, line by line (grep-style), across the call's scope. Choose the "
        + "files with the same selector as find_files (glob primary) and the same cwd scoping: pass cwd (an "
        + "absolute working directory inside the base root) to scope to one project, omit it to search the "
        + "whole base root (the heavy path), or pass cwd @name (a package root from describe_scope, optionally "
        + "@name/<subpath>) to search a dependency cache. Give `pattern` and set `is_regex` for a regex. Matching is per "
        + "line, so a pattern that spans a newline will not match. Returns {path, line, column (1-based), "
        + "text, match_start, match_end}; the path is relative to cwd (or the base root when omitted), and "
        + "column and offsets are UTF-16 code units. Set files_only to list matching files, or count_only for "
        + "{path, match_count} per file (matching lines, exact) with total_match_count, the sum over every page, "
        + "on the envelope. Page with the returned cursor (keep files_only, count_only, and cwd stable across pages).")]
    public Task<ResultEnvelope> InvokeAsync(
        [Description("The content pattern to find. Required, non-empty.")]
        string pattern,
        [Description("Treat `pattern` as a regex (culture-invariant, timeout-guarded). Default false (literal).")]
        bool is_regex = false,
        [Description("A glob selecting which files to search, e.g. \"src/**/*.cs\". Exactly one of glob/regex/paths.")]
        string glob = null,
        [Description("A regex selecting which files to search (over the path, not the content). Exactly one of glob/regex/paths.")]
        string regex = null,
        [Description("Explicit scope-relative files to search. Exactly one of glob/regex/paths.")]
        string[] paths = null,
        [Description("Absolute working directory inside the base root to scope this call to; or @name (a package root from describe_scope), optionally @name/<subpath>, to search a dependency cache. Omit to search the whole base root (the heavy path).")]
        string cwd = null,
        [Description("File extensions to keep (dot optional, case-insensitive). ANDed with the selector.")]
        string[] extensions = null,
        [Description("Globs that re-include otherwise-ignored paths for this call, e.g. [\"build/**\"] for a tracked build directory. Re-includes only the built-in default tier; never re-includes a project ignore-file path, and never bypasses the secret denylist.")]
        string[] include_ignored = null,
        [Description("Match case-sensitively (both file selection and content). Default false.")]
        bool case_sensitive = false,
        [Description("Lines of context to include before and after each match. Default 0; clamped to the ceiling.")]
        int context_lines = 0,
        [Description("Maximum matches returned per file. Default and ceiling from describe_scope.")]
        int max_matches_per_file = 0,
        [Description("Page size: max matches (or files when files_only or count_only). Default and ceiling from describe_scope.")]
        int max_results = 0,
        [Description("Return one entry per matching file instead of each match. Default false.")]
        bool files_only = false,
        [Description("Return one {path, match_count} entry per file with matching lines instead of each match. match_count counts lines, not occurrences, and is exact (max_matches_per_file does not apply); the envelope's total_match_count sums it over every matching file before paging. Pages like files_only, and each page re-walks the selection and re-counts every selected file, so narrow the selector. Cannot be combined with files_only. Default false.")]
        bool count_only = false,
        [Description("Opaque pagination cursor from a previous response; keep files_only, count_only, and cwd stable across pages.")]
        string cursor = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = common.MakeContext("search_text");
        return common.WrapAsync(ctx, () =>
        {
            if (string.IsNullOrEmpty(pattern))
            {
                throw TextSearchException.InvalidArgument("pattern must not be empty");
            }

            if (files_only && count_only)
            {
                throw TextSearchException.InvalidArgument("files_only and count_only are mutually exclusive");
            }

            var scope = resolver.Resolve(cwd);
            common.ScopeEntered(ctx, cwd, scope);

            var selector = SelectorSupport.Build(config, glob, regex, paths, extensions, include_ignored, case_sensitive);
            if (!selector.IncludeIgnored.IsEmpty)
            {
                common.IncludeIgnoredUsed(ctx);
            }

            var matcher = BuildMatcher(ctx, pattern, is_regex, case_sensitive);
            var contextLines = Math.Clamp(context_lines, 0, config.MaxContextLines);
            var perFileCap = Math.Clamp(max_matches_per_file <= 0 ? config.MaxMatchesPerFile : max_matches_per_file, 1, config.MaxMatchesPerFile);
            var pageSize = Math.Clamp(max_results <= 0 ? config.MaxResults : max_results, 1, config.MaxResults);
            using var budget = SelectorSupport.CreateBudget(config, cancellationToken);

            var filters = SelectorSupport
                .Filters(scope.ScopeKey, glob, regex, paths, extensions, include_ignored, case_sensitive)
                .Redact("pattern", pattern)
                .Flag("is_regex", is_regex)
                .Number("context_lines", contextLines)
                .Flag("files_only", files_only)
                .Flag("count_only", count_only)
                .Build();

            Page<object> page;
            int filesScanned;
            int? totalMatchCount = null;
            if (count_only)
            {
                (page, filesScanned, totalMatchCount) = SearchCounts(ctx, scope, selector, matcher, cursor, pageSize, budget.Token);
            }
            else if (files_only)
            {
                (page, filesScanned) = SearchFilesOnly(ctx, scope, selector, matcher, cursor, pageSize, budget.Token);
            }
            else
            {
                (page, filesScanned) = SearchMatches(ctx, scope, selector, matcher, contextLines, perFileCap, cursor, pageSize, budget.Token);
            }

            ctx.Log(
                LogLevel.Debug,
                "files_scanned",
                extras: new Dictionary<string, object>(StringComparer.Ordinal) { [LogFields.FilesScanned] = filesScanned });

            return Task.FromResult(ToolCommon.ListSuccess(
                page.Items,
                truncated: page.Truncated,
                cursor: page.Cursor,
                filtersApplied: filters,
                totalMatchCount: totalMatchCount));
        });
    }

    private (Page<object> Page, int FilesScanned) SearchMatches(
        CallContext ctx,
        CallScope scope,
        FileSelector selector,
        LineMatcher matcher,
        int contextLines,
        int perFileCap,
        string cursor,
        int pageSize,
        CancellationToken budgetToken)
    {
        var skip = string.IsNullOrEmpty(cursor) ? 0 : Cursor.DecodeSearch(scope.CursorScope, cursor);

        SelectorSupport.CheckBudget(config, budgetToken);
        var walk = SelectorSupport.Run(scope.Selection, selector, config, budgetToken);
        var emitted = new List<object>();
        var more = false;
        var seen = 0;
        var filesScanned = 0;

        foreach (var entry in walk.Entries)
        {
            SelectorSupport.CheckBudget(config, budgetToken);
            var outcome = SearchOne(ctx, scope.Reader, entry, matcher, contextLines, perFileCap);
            filesScanned++;
            if (outcome is null)
            {
                continue;
            }

            foreach (var match in outcome.Matches)
            {
                if (seen < skip)
                {
                    seen++;
                    continue;
                }

                if (emitted.Count >= pageSize)
                {
                    more = true;
                    break;
                }

                emitted.Add(match with { Path = entry.RelativePath });
                seen++;
            }

            if (more)
            {
                break;
            }
        }

        // The scope-offset cursor assumes a stable per-file match count across pages. A pattern that trips
        // the match timeout (already a counted refusal) can shift that count, so paged results are
        // best-effort for such abusive patterns; confinement and leak guarantees are unaffected.
        var truncated = more || walk.Truncated;
        var cursorOut = more ? Cursor.EncodeSearch(scope.CursorScope, seen) : null;
        return (new Page<object>(emitted, truncated, cursorOut), filesScanned);
    }

    private (Page<object> Page, int FilesScanned) SearchFilesOnly(
        CallContext ctx,
        CallScope scope,
        FileSelector selector,
        LineMatcher matcher,
        string cursor,
        int pageSize,
        CancellationToken budgetToken)
    {
        SelectorSupport.CheckBudget(config, budgetToken);
        var walk = SelectorSupport.Run(scope.Selection, selector, config, budgetToken);
        var matched = new List<FlatFile>();
        var filesScanned = 0;
        foreach (var entry in walk.Entries)
        {
            SelectorSupport.CheckBudget(config, budgetToken);
            var outcome = SearchOne(ctx, scope.Reader, entry, matcher, contextLines: 0, perFileCap: 1);
            filesScanned++;
            if (outcome is not null && outcome.Matches.Count > 0)
            {
                matched.Add(new FlatFile(entry));
            }
        }

        var page = FileListing.Paginate(matched, walk.Truncated, skippedSymlinks: 0, config, scope.CursorScope, cursor, pageSize);
        var items = page.Items
            .Select(static file => (object)new FileHit
            {
                Path = file.Entry.RelativePath,
                Size = file.Entry.Size,
                LastModified = file.Entry.LastModifiedUtc.ToString("o", CultureInfo.InvariantCulture),
            })
            .ToList();
        return (new Page<object>(items, page.Truncated, page.Cursor), filesScanned);
    }

    private (Page<object> Page, int FilesScanned, int TotalMatchCount) SearchCounts(
        CallContext ctx,
        CallScope scope,
        FileSelector selector,
        LineMatcher matcher,
        string cursor,
        int pageSize,
        CancellationToken budgetToken)
    {
        SelectorSupport.CheckBudget(config, budgetToken);
        var walk = SelectorSupport.Run(scope.Selection, selector, config, budgetToken);
        var matched = new List<FlatFile>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var total = 0;
        var filesScanned = 0;
        foreach (var entry in walk.Entries)
        {
            SelectorSupport.CheckBudget(config, budgetToken);
            var document = TryLoadDocument(ctx, scope.Reader, entry);
            filesScanned++;
            if (document is null)
            {
                continue;
            }

            int matchingLines;
            bool timedOut;
            try
            {
                (matchingLines, timedOut) = ContentSearch.CountMatchingLines(document, matcher, budgetToken);
            }
            catch (OperationCanceledException)
            {
                throw TextSearchException.OperationBudgetExceeded((int)config.OperationBudget.TotalMilliseconds);
            }

            if (timedOut)
            {
                common.RegexTimeout(ctx);
            }

            if (matchingLines > 0)
            {
                matched.Add(new FlatFile(entry));
                counts[entry.RelativePath] = matchingLines;
                total += matchingLines;
            }
        }

        var page = FileListing.Paginate(matched, walk.Truncated, skippedSymlinks: 0, config, scope.CursorScope, cursor, pageSize);
        var items = page.Items
            .Select(file => (object)new FileMatchCount
            {
                Path = file.Entry.RelativePath,
                MatchCount = counts[file.Entry.RelativePath],
            })
            .ToList();
        return (new Page<object>(items, page.Truncated, page.Cursor), filesScanned, total);
    }

    private FileSearchOutcome SearchOne(CallContext ctx, GatedFileReader reader, WalkEntry entry, LineMatcher matcher, int contextLines, int perFileCap)
    {
        var document = TryLoadDocument(ctx, reader, entry);
        if (document is null)
        {
            return null;
        }

        var outcome = ContentSearch.SearchFile(document, matcher, contextLines, perFileCap);
        if (outcome.TimedOut)
        {
            common.RegexTimeout(ctx);
        }

        return outcome;
    }

    /// <summary>Read and decode one walked file through the gate, recording a refusal when it cannot be searched.</summary>
    /// <param name="ctx">The call context the refusal is recorded against.</param>
    /// <param name="reader">The scope's gated reader.</param>
    /// <param name="entry">The walked file.</param>
    /// <returns>The decoded text document, or <c>null</c> when the gate refused the read or the file is binary.</returns>
    private TextDocument TryLoadDocument(CallContext ctx, GatedFileReader reader, WalkEntry entry)
    {
        var read = reader.Read(entry.RelativePath);
        if (!read.IsOk)
        {
            common.Refusal(ctx, RefusalReason.From(read.Status));
            return null;
        }

        var document = TextDocument.Load(read.Bytes, detector);
        if (document.IsBinary)
        {
            common.Refusal(ctx, "binary");
            return null;
        }

        return document;
    }

    private LineMatcher BuildMatcher(CallContext ctx, string pattern, bool isRegex, bool caseSensitive)
    {
        if (!isRegex)
        {
            return LineMatcher.ForLiteral(pattern, caseSensitive);
        }

        CompiledRegex compiled;
        try
        {
            compiled = SafeRegexCompiler.Compile(
                pattern,
                new SafeRegexOptions { MatchTimeout = config.RegexTimeout, CaseSensitive = caseSensitive });
        }
        catch (RegexCompilationException ex)
        {
            throw TextSearchException.PatternInvalid(ex.Message);
        }

        if (!compiled.IsNonBacktracking)
        {
            common.RegexFallback(ctx);
        }

        return LineMatcher.ForRegex(compiled.Regex);
    }
}