using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.TextSearch.Configuration;
using RaccoonNinja.McpToolset.Server.TextSearch.Envelope;
using RaccoonNinja.McpToolset.Server.TextSearch.Logging;
using RaccoonNinja.McpToolset.Server.TextSearch.Models;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tools;

/// <summary>The <c>find_files</c> tool: list files in the call's scope by glob, regex, paths, or everything.</summary>
[McpServerToolType]
public sealed class FindFilesTool(ToolCommon common, SearchConfig config, ScopeResolver resolver)
{
    /// <summary>List the files a selector names in the call's scope, paginated and pruned.</summary>
    /// <param name="glob">A glob over the scope-relative path (primary).</param>
    /// <param name="regex">A regex over the scope-relative path (escape hatch).</param>
    /// <param name="paths">An explicit list of scope-relative paths.</param>
    /// <param name="cwd">An absolute working directory inside the base root to scope the call to; omit for the whole base root.</param>
    /// <param name="extensions">File extensions to keep (dot optional).</param>
    /// <param name="include_ignored">Globs that re-include otherwise-ignored paths (built-in default tier only, e.g. node_modules); never re-includes a .gitignore/.mcpignore path, and never bypasses the secret denylist.</param>
    /// <param name="case_sensitive">Whether matching is case-sensitive.</param>
    /// <param name="max_files">The page size, clamped to the ceiling.</param>
    /// <param name="cursor">A pagination cursor from a previous call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An envelope of file entries with pagination metadata.</returns>
    [McpServerTool(Name = "find_files", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "List files in the call's scope. Give exactly one of: glob (primary, e.g. \"**/*Test.cs\"), regex, "
        + "or an explicit paths list; give none to list everything. A glob with no slash matches the basename "
        + "at any depth (so \"*.cs\" is recursive). Pass cwd (an absolute working directory inside the base "
        + "root) to scope the call to one project; omit it to search the whole base root, the heavy path. Pass "
        + "cwd @name (a package root from describe_scope) to search a dependency cache; add /<subpath> to scope "
        + "to one package. Paths (in and out) are relative to cwd, or to the base root when cwd is omitted. "
        + "Page with the returned cursor (keep cwd stable across pages).")]
    public Task<ResultEnvelope> InvokeAsync(
        [Description("A glob over the scope-relative path, e.g. \"src/**/*.cs\". Exactly one of glob/regex/paths.")]
        string glob = null,
        [Description("A regex over the scope-relative path, when a glob cannot express it. Exactly one of glob/regex/paths.")]
        string regex = null,
        [Description("Explicit scope-relative paths to return. Exactly one of glob/regex/paths.")]
        string[] paths = null,
        [Description("Absolute working directory inside the base root to scope this call to; or @name (a package root from describe_scope), optionally @name/<subpath>, to search a dependency cache. Omit to search the whole base root (the heavy path).")]
        string cwd = null,
        [Description("File extensions to keep (dot optional, case-insensitive), e.g. [\"cs\",\"rs\"]. ANDed with the selector.")]
        string[] extensions = null,
        [Description("Globs that re-include otherwise-ignored paths for this call, e.g. [\"node_modules/**\"]. Re-includes only the built-in default tier; never re-includes a .gitignore/.mcpignore path, and never bypasses the secret denylist.")]
        string[] include_ignored = null,
        [Description("Match case-sensitively. Default false.")]
        bool case_sensitive = false,
        [Description("Maximum files to return in this page; clamped to the server ceiling.")]
        int max_files = 0,
        [Description("Opaque pagination cursor from a previous response; omit for the first page.")]
        string cursor = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = common.MakeContext("find_files");
        return common.WrapAsync(ctx, () =>
        {
            var scope = resolver.Resolve(cwd);
            common.ScopeEntered(ctx, cwd, scope);

            var selector = SelectorSupport.Build(config, glob, regex, paths, extensions, include_ignored, case_sensitive);
            if (!selector.IncludeIgnored.IsEmpty)
            {
                common.IncludeIgnoredUsed(ctx);
            }

            using var budget = SelectorSupport.CreateBudget(config, cancellationToken);
            var (files, windowTruncated, skippedSymlinks) = FileListing.Walk(scope, selector, config, budget.Token);
            var pageSize = SelectorSupport.PageSize(config, max_files);
            var page = FileListing.Paginate(files, windowTruncated, skippedSymlinks, config, scope.CursorScope, cursor, pageSize);

            ctx.Log(
                LogLevel.Debug,
                "files_scanned",
                extras: new Dictionary<string, object>(StringComparer.Ordinal) { [LogFields.FilesScanned] = files.Count });

            var filters = SelectorSupport
                .Filters(scope.ScopeKey, glob, regex, paths, extensions, include_ignored, case_sensitive)
                .Number("max_files", pageSize)
                .Build();

            return Task.FromResult(ToolCommon.ListSuccess(
                page.Items.Select(ToHit),
                truncated: page.Truncated,
                cursor: page.Cursor,
                filtersApplied: filters,
                skippedSymlinks: page.SkippedSymlinks));
        });
    }

    private static FileHit ToHit(FlatFile file)
        => new()
        {
            Path = file.Entry.RelativePath,
            Size = file.Entry.Size,
            LastModified = file.Entry.LastModifiedUtc.ToString("o", CultureInfo.InvariantCulture),
        };
}