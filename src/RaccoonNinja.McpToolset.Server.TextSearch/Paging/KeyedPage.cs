namespace RaccoonNinja.McpToolset.Server.TextSearch.Paging;

/// <summary>One page from the keyed paginator, before the cursor is encoded.</summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Items">The items on this page.</param>
/// <param name="Truncated">Whether more results exist beyond this page (in the window or past its ceiling).</param>
/// <param name="NextKey">The composite key to resume after, or <c>null</c> when there is no next page.</param>
public sealed record KeyedPage<T>(IReadOnlyList<T> Items, bool Truncated, string NextKey);