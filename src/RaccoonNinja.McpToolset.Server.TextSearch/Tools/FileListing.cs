using RaccoonNinja.McpToolset.Files.Selection;
using RaccoonNinja.McpToolset.Server.TextSearch.Configuration;
using RaccoonNinja.McpToolset.Server.TextSearch.Paging;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tools;

/// <summary>
/// Walks the targeted roots into one flattened, ordinal-ordered window and pages it. The walk is bounded
/// by the per-call op budget between roots and the aggregate window ceiling; paging is the shared,
/// boundary-safe <see cref="Paginator.ByKey{T}"/> over the composite <c>{rootIndex}\0{path}</c> key.
/// </summary>
internal static class FileListing
{
    /// <summary>Walk every target root and flatten the entries into one (root, path)-ordered list.</summary>
    /// <param name="targets">The resolved target roots, in order.</param>
    /// <param name="selector">The validated selector.</param>
    /// <param name="config">The server config.</param>
    /// <param name="budgetToken">The token cancelled at the operation deadline, bounding each root's walk.</param>
    /// <returns>The flattened files, whether any root's window was capped, and the skipped-symlink total.</returns>
    public static (List<FlatFile> Files, bool WindowTruncated, int SkippedSymlinks) Walk(
        IReadOnlyList<RootSpec> targets,
        FileSelector selector,
        SearchConfig config,
        CancellationToken budgetToken)
    {
        var files = new List<FlatFile>();
        var windowTruncated = false;
        var skippedSymlinks = 0;
        for (var rootIndex = 0; rootIndex < targets.Count; rootIndex++)
        {
            SelectorSupport.CheckBudget(config, budgetToken);
            var walk = SelectorSupport.Run(targets[rootIndex].Selection, selector, config, budgetToken);
            foreach (var entry in walk.Entries)
            {
                files.Add(new FlatFile(rootIndex, targets[rootIndex].Name, entry));
            }

            windowTruncated |= walk.Truncated;
            skippedSymlinks += walk.SkippedSymlinks;
        }

        return (files, windowTruncated, skippedSymlinks);
    }

    /// <summary>Apply the aggregate window ceiling, then page the flattened list after the cursor.</summary>
    /// <param name="flattened">The flattened, (root, path)-ordered files.</param>
    /// <param name="windowTruncated">Whether the window was already capped during the walk.</param>
    /// <param name="skippedSymlinks">The aggregate skipped-symlink count to carry through.</param>
    /// <param name="config">The server config (supplies the aggregate ceiling).</param>
    /// <param name="target">The request's <c>root</c> target, for cursor pinning.</param>
    /// <param name="cursor">The incoming cursor, or null.</param>
    /// <param name="pageSize">The page size.</param>
    /// <returns>The page.</returns>
    /// <exception cref="Errors.TextSearchException">Thrown when the cursor is malformed or for a different target.</exception>
    public static FileListPage Paginate(
        List<FlatFile> flattened,
        bool windowTruncated,
        int skippedSymlinks,
        SearchConfig config,
        string target,
        string cursor,
        int pageSize)
    {
        var window = flattened;
        if (window.Count > config.MaxFilesCeiling)
        {
            windowTruncated = true;
            window = window.GetRange(0, config.MaxFilesCeiling);
        }

        var afterKey = string.IsNullOrEmpty(cursor) ? null : Cursor.DecodeList(target, cursor);
        var page = Paginator.ByKey(
            window,
            static file => SelectorSupport.Key(file.RootIndex, file.Entry.RelativePath),
            afterKey,
            pageSize,
            windowTruncated);

        var cursorOut = page.NextKey is null ? null : Cursor.EncodeList(target, page.NextKey);
        return new FileListPage(page.Items, page.Truncated, cursorOut, skippedSymlinks);
    }
}