using RaccoonNinja.McpToolset.Files.Selection;
using RaccoonNinja.McpToolset.Server.TextSearch.Configuration;
using RaccoonNinja.McpToolset.Server.TextSearch.Paging;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tools;

/// <summary>
/// Walks one call scope into an ordinal-ordered window and pages it. The walk is bounded by the per-call
/// op budget and the window ceiling; paging is the shared, boundary-safe <see cref="Paginator.ByKey{T}"/>
/// over the scope-relative path, and the cursor is pinned to the scope key.
/// </summary>
internal static class FileListing
{
    /// <summary>Walk the scope and collect its matched entries in scope-relative order.</summary>
    /// <param name="scope">The resolved call scope.</param>
    /// <param name="selector">The validated selector.</param>
    /// <param name="config">The server config.</param>
    /// <param name="budgetToken">The token cancelled at the operation deadline, bounding the walk.</param>
    /// <returns>The walked files, whether the window was capped, and the skipped-symlink count.</returns>
    public static (List<FlatFile> Files, bool WindowTruncated, int SkippedSymlinks) Walk(
        CallScope scope,
        FileSelector selector,
        SearchConfig config,
        CancellationToken budgetToken)
    {
        SelectorSupport.CheckBudget(config, budgetToken);
        var walk = SelectorSupport.Run(scope.Selection, selector, config, budgetToken);
        var files = new List<FlatFile>(walk.Entries.Count);
        foreach (var entry in walk.Entries)
        {
            files.Add(new FlatFile(entry));
        }

        return (files, walk.Truncated, walk.SkippedSymlinks);
    }

    /// <summary>Apply the window ceiling, then page the list after the cursor.</summary>
    /// <param name="walked">The walked, path-ordered files.</param>
    /// <param name="windowTruncated">Whether the window was already capped during the walk.</param>
    /// <param name="skippedSymlinks">The skipped-symlink count to carry through.</param>
    /// <param name="config">The server config (supplies the window ceiling).</param>
    /// <param name="cursorScope">The call's cursor identity (kind plus scope key), for cursor pinning.</param>
    /// <param name="cursor">The incoming cursor, or null.</param>
    /// <param name="pageSize">The page size.</param>
    /// <returns>The page.</returns>
    /// <exception cref="Errors.TextSearchException">Thrown when the cursor is malformed or for a different scope.</exception>
    public static FileListPage Paginate(
        List<FlatFile> walked,
        bool windowTruncated,
        int skippedSymlinks,
        SearchConfig config,
        string cursorScope,
        string cursor,
        int pageSize)
    {
        var window = walked;
        if (window.Count > config.MaxFilesCeiling)
        {
            windowTruncated = true;
            window = window.GetRange(0, config.MaxFilesCeiling);
        }

        var afterKey = string.IsNullOrEmpty(cursor) ? null : Cursor.DecodeList(cursorScope, cursor);
        var page = Paginator.ByKey(
            window,
            static file => file.Entry.RelativePath,
            afterKey,
            pageSize,
            windowTruncated);

        var cursorOut = page.NextKey is null ? null : Cursor.EncodeList(cursorScope, page.NextKey);
        return new FileListPage(page.Items, page.Truncated, cursorOut, skippedSymlinks);
    }
}