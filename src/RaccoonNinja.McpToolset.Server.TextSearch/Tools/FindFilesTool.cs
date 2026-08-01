using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.TextSearch.Configuration;
using RaccoonNinja.McpToolset.Server.TextSearch.Envelope;
using RaccoonNinja.McpToolset.Server.TextSearch.Models;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tools;

/// <summary>The <c>find_files</c> tool: list files across the targeted roots by glob, regex, paths, or everything.</summary>
[McpServerToolType]
public sealed class FindFilesTool(ToolCommon common, SearchConfig config, RootRegistry registry)
{
    /// <summary>List the files a selector names across the targeted roots, paginated and pruned.</summary>
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
    /// <returns>An envelope of file entries with pagination metadata.</returns>
    [McpServerTool(Name = "find_files", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "List files across the targeted roots. Give exactly one of: glob (primary, e.g. \"**/*Test.cs\"), "
        + "regex, or an explicit paths list; give none to list everything. A glob with no slash matches the "
        + "basename at any depth (so \"*.cs\" is recursive). Target one root by name, all workspace roots by "
        + "default, all package roots with root \"@packages\", or every root with \"@all\"; a package-root "
        + "search must be narrowed by glob, regex, paths, or extensions. Each result carries its root name "
        + "and a root-relative path. Page with the returned cursor.")]
    public Task<ResultEnvelope> InvokeAsync(
        [Description("A glob over the root-relative path, e.g. \"src/**/*.cs\". Exactly one of glob/regex/paths.")]
        string glob = null,
        [Description("A regex over the root-relative path, when a glob cannot express it. Exactly one of glob/regex/paths.")]
        string regex = null,
        [Description("Explicit root-relative paths to return. Exactly one of glob/regex/paths.")]
        string[] paths = null,
        [Description("Target root: a name from describe_scope, \"@packages\", \"@all\", or omitted for all workspace roots.")]
        string root = null,
        [Description("File extensions to keep (dot optional, case-insensitive), e.g. [\"cs\",\"rs\"]. ANDed with the selector.")]
        string[] extensions = null,
        [Description("Include files matched by ignore rules (.gitignore/.mcpignore). Default false; never bypasses the secret denylist.")]
        bool include_ignored = false,
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

            var filters = SelectorSupport
                .Filters(root, glob, regex, paths, extensions, include_ignored, case_sensitive)
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
            Root = file.RootName,
            Path = file.Entry.RelativePath,
            Size = file.Entry.Size,
            LastModified = file.Entry.LastModifiedUtc.ToString("o", CultureInfo.InvariantCulture),
        };
}