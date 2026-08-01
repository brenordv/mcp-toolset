namespace RaccoonNinja.McpToolset.Server.TextSearch.Tools;

/// <summary>A paginated page of flattened cross-root files, with the encoded cursor and skip count.</summary>
/// <param name="Items">The files on this page.</param>
/// <param name="Truncated">Whether more results exist beyond this page.</param>
/// <param name="Cursor">The encoded continuation cursor, or <c>null</c>.</param>
/// <param name="SkippedSymlinks">The aggregate count of symlinked entries pruned across the targeted roots.</param>
internal sealed record FileListPage(
    IReadOnlyList<FlatFile> Items,
    bool Truncated,
    string Cursor,
    int SkippedSymlinks);