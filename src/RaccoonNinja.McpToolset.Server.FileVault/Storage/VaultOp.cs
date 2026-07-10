namespace RaccoonNinja.McpToolset.Server.FileVault.Storage;

/// <summary>The kind of write that produced a version. Persisted in <c>versions.op</c>.</summary>
public enum VaultOp
{
    /// <summary>A full-content save.</summary>
    Save,

    /// <summary>An append to the previous version's content.</summary>
    Append,

    /// <summary>A markdown section replacement.</summary>
    EditSection,

    /// <summary>A JSON/YAML key-path edit.</summary>
    EditKey,

    /// <summary>Vestigial value kept for store compatibility; nothing writes it in v1.1.</summary>
    Restore,
}