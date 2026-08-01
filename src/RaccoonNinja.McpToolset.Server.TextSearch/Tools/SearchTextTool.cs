using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Files.Selection;
using RaccoonNinja.McpToolset.Files.Text;
using RaccoonNinja.McpToolset.Server.TextSearch.Configuration;
using RaccoonNinja.McpToolset.Server.TextSearch.Content;
using RaccoonNinja.McpToolset.Server.TextSearch.Envelope;
using RaccoonNinja.McpToolset.Server.TextSearch.Errors;
using RaccoonNinja.McpToolset.Server.TextSearch.Models;
using RaccoonNinja.McpToolset.Server.TextSearch.Paging;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tools;

/// <summary>The <c>search_text</c> tool: line-oriented literal or regex search across the targeted roots.</summary>
[McpServerToolType]
public sealed class SearchTextTool(ToolCommon common, SearchConfig config, RootRegistry registry, IEncodingDetector detector)
{
    /// <summary>Search file contents for a literal or regex pattern, line by line, across the targeted roots.</summary>
    /// <param name="pattern">The content pattern to find.</param>
    /// <param name="is_regex">Whether <paramref name="pattern"/> is a regex.</param>
    /// <param name="glob">A glob selecting which files to search.</param>
    /// <param name="regex">A regex selecting which files to search (over the path, not the content).</param>
    /// <param name="paths">Explicit files to search.</param>
    /// <param name="root">The target root: a name, <c>@packages</c>, <c>@all</c>, or omitted (all workspace roots).</param>
    /// <param name="extensions">File extensions to keep (dot optional).</param>
    /// <param name="include_ignored">Whether to include ignored files.</param>
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
        "Search file contents for a pattern, line by line (grep-style), across the targeted roots. Choose "
        + "the files with the same selector as find_files (glob primary) and the same root targeting "
        + "(name, \"@packages\", \"@all\", or omitted for all workspace roots); a package-root search must "
        + "be narrowed by glob, regex, paths, or extensions. Give `pattern` and set `is_regex` for a "
        + "regex. Matching is per line, so a pattern that spans a newline will not match. Returns "
        + "{root, path, line, column (1-based), text, match_start, match_end}; column and offsets are "
        + "UTF-16 code units. Set files_only to list matching files. Page with the returned cursor "
        + "(keep files_only and root stable across pages).")]
    public Task<ResultEnvelope> InvokeAsync(
        [Description("The content pattern to find. Required, non-empty.")]
        string pattern,
        [Description("Treat `pattern` as a regex (culture-invariant, timeout-guarded). Default false (literal).")]
        bool is_regex = false,
        [Description("A glob selecting which files to search, e.g. \"src/**/*.cs\". Exactly one of glob/regex/paths.")]
        string glob = null,
        [Description("A regex selecting which files to search (over the path, not the content). Exactly one of glob/regex/paths.")]
        string regex = null,
        [Description("Explicit root-relative files to search. Exactly one of glob/regex/paths.")]
        string[] paths = null,
        [Description("Target root: a name, \"@packages\", \"@all\", or omitted for all workspace roots.")]
        string root = null,
        [Description("File extensions to keep (dot optional, case-insensitive). ANDed with the selector; narrows package searches.")]
        string[] extensions = null,
        [Description("Include files matched by ignore rules. Default false; never bypasses the secret denylist.")]
        bool include_ignored = false,
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
        [Description("Opaque pagination cursor from a previous response; keep files_only and root stable across pages.")]
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

            var targets = registry.Resolve(root);
            var selector = SelectorSupport.Build(config, glob, regex, paths, extensions, include_ignored, case_sensitive);
            SelectorSupport.EnsureNarrowedForPackages(targets, selector);
            if (SelectorSupport.TargetsPackage(targets))
            {
                common.PackageTargeted(ctx);
            }

            var matcher = BuildMatcher(ctx, pattern, is_regex, case_sensitive);
            var contextLines = Math.Clamp(context_lines, 0, config.MaxContextLines);
            var perFileCap = Math.Clamp(max_matches_per_file <= 0 ? config.MaxMatchesPerFile : max_matches_per_file, 1, config.MaxMatchesPerFile);
            var pageSize = Math.Clamp(max_results <= 0 ? config.MaxResults : max_results, 1, config.MaxResults);
            using var budget = SelectorSupport.CreateBudget(config, cancellationToken);

            var filters = SelectorSupport
                .Filters(root, glob, regex, paths, extensions, include_ignored, case_sensitive)
                .Redact("pattern", pattern)
                .Flag("is_regex", is_regex)
                .Number("context_lines", contextLines)
                .Flag("files_only", files_only)
                .Build();

            var result = files_only
                ? SearchFilesOnly(ctx, targets, selector, matcher, root, cursor, pageSize, budget.Token)
                : SearchMatches(ctx, targets, selector, matcher, contextLines, perFileCap, root, cursor, pageSize, budget.Token);

            return Task.FromResult(ToolCommon.ListSuccess(
                result.Items,
                truncated: result.Truncated,
                cursor: result.Cursor,
                filtersApplied: filters));
        });
    }

    private Page<object> SearchMatches(
        CallContext ctx,
        IReadOnlyList<RootSpec> targets,
        FileSelector selector,
        LineMatcher matcher,
        int contextLines,
        int perFileCap,
        string target,
        string cursor,
        int pageSize,
        CancellationToken budgetToken)
    {
        var (cursorRoot, skip) = string.IsNullOrEmpty(cursor) ? (null, 0) : Cursor.DecodeSearch(target, cursor);
        var resuming = cursorRoot is not null;
        var reachedCursorRoot = !resuming;
        var emitted = new List<object>();
        var more = false;
        var windowTruncated = false;
        string lastRoot = null;
        var lastOffset = 0;

        foreach (var spec in targets)
        {
            SelectorSupport.CheckBudget(config, budgetToken);

            int rootSkip;
            if (!reachedCursorRoot)
            {
                if (!spec.Name.Equals(cursorRoot, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                reachedCursorRoot = true;
                rootSkip = skip;
            }
            else
            {
                rootSkip = 0;
            }

            var walk = SelectorSupport.Run(spec.Selection, selector, config, budgetToken);
            windowTruncated |= walk.Truncated;
            var seenInRoot = 0;

            foreach (var entry in walk.Entries)
            {
                SelectorSupport.CheckBudget(config, budgetToken);
                var outcome = SearchOne(ctx, spec.Reader, entry, matcher, contextLines, perFileCap);
                if (outcome is null)
                {
                    continue;
                }

                foreach (var match in outcome.Matches)
                {
                    if (seenInRoot < rootSkip)
                    {
                        seenInRoot++;
                        continue;
                    }

                    if (emitted.Count >= pageSize)
                    {
                        more = true;
                        break;
                    }

                    emitted.Add(match with { Root = spec.Name, Path = entry.RelativePath });
                    seenInRoot++;
                    lastRoot = spec.Name;
                    lastOffset = seenInRoot;
                }

                if (more)
                {
                    break;
                }
            }

            if (more)
            {
                break;
            }
        }

        if (resuming && !reachedCursorRoot)
        {
            throw TextSearchException.InvalidArgument("cursor is for a different query");
        }

        // The per-root offset cursor assumes a stable per-file match count across pages. A pattern that
        // trips the match timeout (already a counted refusal) can shift that count, so paged results are
        // best-effort for such abusive patterns; confinement and leak guarantees are unaffected.
        var truncated = more || windowTruncated;
        var cursorOut = more ? Cursor.EncodeSearch(target, lastRoot, lastOffset) : null;
        return new Page<object>(emitted, truncated, cursorOut);
    }

    private Page<object> SearchFilesOnly(
        CallContext ctx,
        IReadOnlyList<RootSpec> targets,
        FileSelector selector,
        LineMatcher matcher,
        string target,
        string cursor,
        int pageSize,
        CancellationToken budgetToken)
    {
        var matched = new List<FlatFile>();
        var windowTruncated = false;
        for (var rootIndex = 0; rootIndex < targets.Count; rootIndex++)
        {
            SelectorSupport.CheckBudget(config, budgetToken);
            var walk = SelectorSupport.Run(targets[rootIndex].Selection, selector, config, budgetToken);
            windowTruncated |= walk.Truncated;
            foreach (var entry in walk.Entries)
            {
                SelectorSupport.CheckBudget(config, budgetToken);
                var outcome = SearchOne(ctx, targets[rootIndex].Reader, entry, matcher, contextLines: 0, perFileCap: 1);
                if (outcome is not null && outcome.Matches.Count > 0)
                {
                    matched.Add(new FlatFile(rootIndex, targets[rootIndex].Name, entry));
                }
            }
        }

        var page = FileListing.Paginate(matched, windowTruncated, skippedSymlinks: 0, config, target, cursor, pageSize);
        var items = page.Items
            .Select(static file => (object)new FileHit
            {
                Root = file.RootName,
                Path = file.Entry.RelativePath,
                Size = file.Entry.Size,
                LastModified = file.Entry.LastModifiedUtc.ToString("o", CultureInfo.InvariantCulture),
            })
            .ToList();
        return new Page<object>(items, page.Truncated, page.Cursor);
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