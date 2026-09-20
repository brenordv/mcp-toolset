namespace RaccoonNinja.McpToolset.Server.FileVault.Services;

/// <summary>A decoded <c>vault_list</c> cursor: the sort key of the last row on the previous page.</summary>
/// <param name="UpdatedAt">The row's <c>updated_at</c>, Unix epoch seconds.</param>
/// <param name="Project">The row's project namespace.</param>
/// <param name="Name">The row's name.</param>
public readonly record struct CursorKey(long UpdatedAt, string Project, string Name);