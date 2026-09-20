using RaccoonNinja.McpToolset.Server.FileVault.Storage;

namespace RaccoonNinja.McpToolset.Server.FileVault.Services;

/// <summary>
/// The service-level result of a <c>vault_list</c> page: the rows on this page plus the pagination
/// state the tool reports. <see cref="Cursor"/> is <c>null</c> on the final page and in fallback
/// mode (which never paginates); <see cref="QueryMode"/> is <c>null</c> when no query was given.
/// </summary>
public sealed record ListPageResult
{
    /// <summary>The rows on this page, in canonical list order.</summary>
    public IReadOnlyList<FileSummaryRow> Items { get; init; } = [];

    /// <summary>The number of rows on this page.</summary>
    public int Count { get; init; }

    /// <summary>Whether rows beyond this page existed.</summary>
    public bool Truncated { get; init; }

    /// <summary>The cursor to fetch the next page, or <c>null</c> when there is none to issue.</summary>
    public string Cursor { get; init; }

    /// <summary>Which matching pass produced the results, or <c>null</c> when no query was given.</summary>
    public ListMode? QueryMode { get; init; }
}