using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Files.Selection;
using RaccoonNinja.McpToolset.Files.Text;
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
    /// <summary>Search file contents for a literal or regex pattern, line by line, across the call's scope.</summary>
    /// <param name="pattern">The content pattern to find.</param>
    /// <param name="is_regex">Whether <paramref name="pattern"/> is a regex.</param>
    /// <param name="glob">A glob selecting which files to search.</param>
    /// <param name="regex">A regex selecting which files to search (over the path, not the content).</param>
    /// <param name="paths">Explicit files to search.</param>
    /// <param name="cwd">An absolute working directory inside the base root to scope the call to; omit for the whole base root.</param>
    /// <param name="extensions">File extensions to keep (dot optional).</param>
    /// <param name="include_ignored">Globs that re-include otherwise-ignored paths for this call; never bypasses the secret denylist.</param>
    /// <param name="case_sensitive">Whether both file matching and content matching are case-sensitive.</param>
    /// <param name="context_lines">Lines of context around each match; clamped to the ceiling.</param>
    /// <param name="max_matches_per_file">The per-file match cap; clamped to the ceiling.</param>
    /// <param name="max_results">The page size (matches, or files when files_only); clamped to the ceiling.</param>
    /// <param name="files_only">Return one entry per matching file instead of each match.</param>
    /// <param name="cursor">A pagination cursor from a previous call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An envelope of matches (or matching files) with pagination metadata.</returns>
    [McpServerTool(Name = "search_text", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Search file contents for a pattern, line by line (grep-style), across the call's scope. Choose the "
        + "files with the same selector as find_files (glob primary) and the same cwd scoping: pass cwd (an "
        + "absolute working directory inside the base root) to scope to one project, omit it to search the "
        + "whole base root (the heavy path), or pass cwd @name (a package root from describe_scope, optionally "
        + "@name/<subpath>) to search a dependency cache. Give `pattern` and set `is_regex` for a regex. Matching is per "
        + "line, so a pattern that spans a newline will not match. Returns {path, line, column (1-based), "
        + "text, match_start, match_end}; the path is relative to cwd (or the base root when omitted), and "
        + "column and offsets are UTF-16 code units. Set files_only to list matching files. Page with the "
        + "returned cursor (keep files_only and cwd stable across pages).")]
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
        [Description("Globs that re-include otherwise-ignored paths for this call, e.g. [\"node_modules/**\"]. Omit/empty keeps every ignore tier; never bypasses the secret denylist.")]
        string[] include_ignored = null,
        [Description("Match case-sensitively (both file selection and content). Default false.")]
        bool case_sensitive = false,
        [Description("Lines of context to include before and after each match. Default 0; clamped to the ceiling.")]
        int context_lines = 0,
        [Description("Maximum matches returned per file. Default and ceiling from describe_scope.")]
        int max_matches_per_file = 0,
        [Description("Page size: max matches (or files when files_only). Default and ceiling from describe_scope.")]
        int max_results = 0,
        [Description("Return one entry per matching file instead of each match. Default false.")]
        bool files_only = false,
        [Description("Opaque pagination cursor from a previous response; keep files_only and cwd stable across pages.")]
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
                .Build();

            var (page, filesScanned) = files_only
                ? SearchFilesOnly(ctx, scope, selector, matcher, cursor, pageSize, budget.Token)
                : SearchMatches(ctx, scope, selector, matcher, contextLines, perFileCap, cursor, pageSize, budget.Token);

            ctx.Log(
                LogLevel.Debug,
                "files_scanned",
                extras: new Dictionary<string, object>(StringComparer.Ordinal) { [LogFields.FilesScanned] = filesScanned });

            return Task.FromResult(ToolCommon.ListSuccess(
                page.Items,
                truncated: page.Truncated,
                cursor: page.Cursor,
                filtersApplied: filters));
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

    private FileSearchOutcome SearchOne(CallContext ctx, GatedFileReader reader, WalkEntry entry, LineMatcher matcher, int contextLines, int perFileCap)
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

        var outcome = ContentSearch.SearchFile(document, matcher, contextLines, perFileCap);
        if (outcome.TimedOut)
        {
            common.RegexTimeout(ctx);
        }

        return outcome;
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