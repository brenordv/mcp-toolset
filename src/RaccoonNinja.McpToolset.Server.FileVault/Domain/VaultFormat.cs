namespace RaccoonNinja.McpToolset.Server.FileVault.Domain;

/// <summary>
/// The content format of a stored file. Drives the on-disk snapshot extension and gates the
/// structure-aware editors (<c>edit_section</c> is markdown-only; <c>edit_key</c> is JSON/YAML-only).
/// </summary>
public enum VaultFormat
{
    /// <summary>Plain text (the default).</summary>
    Text,

    /// <summary>Markdown, editable via <c>vault_edit_section</c>.</summary>
    Markdown,

    /// <summary>JSON, editable via <c>vault_edit_key</c>.</summary>
    Json,

    /// <summary>YAML, editable via <c>vault_edit_key</c>.</summary>
    Yaml,
}