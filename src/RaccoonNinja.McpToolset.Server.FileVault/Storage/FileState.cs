namespace RaccoonNinja.McpToolset.Server.FileVault.Storage;

/// <summary>The lifecycle state of a file. Persisted in <c>files.state</c>.</summary>
public enum FileState
{
    /// <summary>Visible in listings and writable.</summary>
    Active,

    /// <summary>Soft-deleted: hidden from listings, rejected by writes, restorable.</summary>
    Archived,
}