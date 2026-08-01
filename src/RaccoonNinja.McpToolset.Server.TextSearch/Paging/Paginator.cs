namespace RaccoonNinja.McpToolset.Server.TextSearch.Paging;

/// <summary>
/// Pages an ordinal-sorted result set by resuming after a composite key. Multi-file tools flatten every
/// targeted root into one list keyed <c>{rootIndex}\0{path}</c>, already ordered by (root, path), and
/// page it here in one pass, so no root is lost at a page boundary and a page never comes back empty
/// while more results remain. The skip is ordinal, matching how the flattened list is built.
/// </summary>
public static class Paginator
{
    /// <summary>Page <paramref name="sorted"/> by resuming after <paramref name="afterKey"/>.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="sorted">The full window, ordinal-sorted by the key <paramref name="keyOf"/> returns.</param>
    /// <param name="keyOf">Extracts an item's composite ordinal key.</param>
    /// <param name="afterKey">The key to resume after, or <c>null</c> for the first page.</param>
    /// <param name="pageSize">The number of items per page.</param>
    /// <param name="windowTruncated">Whether the window itself was capped (more exist past it, unpageable).</param>
    /// <returns>The page and the key to resume after next.</returns>
    public static KeyedPage<T> ByKey<T>(
        IReadOnlyList<T> sorted,
        Func<T, string> keyOf,
        string afterKey,
        int pageSize,
        bool windowTruncated)
    {
        ArgumentNullException.ThrowIfNull(sorted);
        ArgumentNullException.ThrowIfNull(keyOf);

        var start = 0;
        if (!string.IsNullOrEmpty(afterKey))
        {
            while (start < sorted.Count && string.CompareOrdinal(keyOf(sorted[start]), afterKey) <= 0)
            {
                start++;
            }
        }

        var page = new List<T>(Math.Min(pageSize, Math.Max(0, sorted.Count - start)));
        for (var i = start; i < sorted.Count && page.Count < pageSize; i++)
        {
            page.Add(sorted[i]);
        }

        var morePagesInWindow = start + page.Count < sorted.Count;
        var nextKey = morePagesInWindow ? keyOf(page[^1]) : null;
        return new KeyedPage<T>(page, morePagesInWindow || windowTruncated, nextKey);
    }
}