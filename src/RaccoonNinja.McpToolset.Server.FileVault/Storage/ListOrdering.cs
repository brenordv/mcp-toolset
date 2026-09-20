namespace RaccoonNinja.McpToolset.Server.FileVault.Storage;

/// <summary>
/// The single canonical listing order, shared by the repository, the service, and the cursor skip
/// logic so pages never overlap or gap. The order is <c>updated_at</c> DESC, then <c>name</c> ASC,
/// then <c>project</c> ASC, with every string comparison ordinal. Names and projects are
/// ASCII-constrained by validation, so ordinal equals SQLite's BINARY collation; a
/// culture-sensitive comparison would diverge and must never be used here.
/// </summary>
public static class ListOrdering
{
    /// <summary>The canonical row comparer built on <see cref="CompareKeys"/>.</summary>
    public static readonly IComparer<FileSummaryRow> Comparer = Comparer<FileSummaryRow>.Create(
        static (a, b) => CompareKeys(a.UpdatedAt, a.Name, a.Project, b.UpdatedAt, b.Name, b.Project));

    /// <summary>
    /// Compare two rows by their raw sort keys. Works across row types (the summary row and the
    /// search candidate) because it takes the raw values rather than a specific record.
    /// </summary>
    /// <returns>Negative when the first sorts earlier, positive when later, zero when equal.</returns>
    public static int CompareKeys(
        long updatedAtA,
        string nameA,
        string projectA,
        long updatedAtB,
        string nameB,
        string projectB)
    {
        var byUpdated = updatedAtB.CompareTo(updatedAtA);
        if (byUpdated != 0)
        {
            return byUpdated;
        }

        var byName = string.CompareOrdinal(nameA, nameB);
        return byName != 0 ? byName : string.CompareOrdinal(projectA, projectB);
    }

    /// <summary>
    /// True when <paramref name="row"/> sorts strictly after the decoded cursor key, i.e. it
    /// belongs on a later page and has not been shown yet.
    /// </summary>
    /// <param name="row">The candidate row.</param>
    /// <param name="cursorUpdatedAt">The cursor's <c>updated_at</c>.</param>
    /// <param name="cursorName">The cursor's name.</param>
    /// <param name="cursorProject">The cursor's project.</param>
    /// <returns><c>true</c> to include the row on the next page.</returns>
    public static bool IsAfterCursor(FileSummaryRow row, long cursorUpdatedAt, string cursorName, string cursorProject)
        => CompareKeys(row.UpdatedAt, row.Name, row.Project, cursorUpdatedAt, cursorName, cursorProject) > 0;
}