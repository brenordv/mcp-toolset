namespace RaccoonNinja.McpToolset.Server.TextSearch.Paging;

/// <summary>One page of results plus its pagination metadata.</summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Items">The items on this page.</param>
/// <param name="Truncated">Whether more results exist beyond this page.</param>
/// <param name="Cursor">The token to fetch the next page, or <c>null</c> when there is none.</param>
public sealed record Page<T>(IReadOnlyList<T> Items, bool Truncated, string Cursor);