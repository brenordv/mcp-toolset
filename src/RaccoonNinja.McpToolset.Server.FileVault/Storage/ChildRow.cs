namespace RaccoonNinja.McpToolset.Server.FileVault.Storage;

/// <summary>A lightweight child row for <c>vault_get</c>'s related-notes listing.</summary>
public sealed record ChildRow
{
    /// <summary>The child file's name.</summary>
    public string Name { get; init; }

    /// <summary>The child file's current one-line summary.</summary>
    public string Summary { get; init; }
}