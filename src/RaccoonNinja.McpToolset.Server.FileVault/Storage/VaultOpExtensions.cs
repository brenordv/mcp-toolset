namespace RaccoonNinja.McpToolset.Server.FileVault.Storage;

/// <summary>Database string conversions for <see cref="VaultOp"/>.</summary>
public static class VaultOpExtensions
{
    /// <summary>The lowercase string persisted in <c>versions.op</c>.</summary>
    /// <param name="op">The operation kind.</param>
    /// <returns>The database string.</returns>
    public static string ToDbString(this VaultOp op)
        => op switch
        {
            VaultOp.Append => "append",
            VaultOp.EditSection => "edit_section",
            VaultOp.EditKey => "edit_key",
            VaultOp.Restore => "restore",
            _ => "save",
        };

    /// <summary>
    /// Parse a stored op string. Unknown values map to <see cref="VaultOp.Save"/>, matching the
    /// Rust server's tolerant read path so legacy stores keep loading.
    /// </summary>
    /// <param name="value">The stored string.</param>
    /// <returns>The parsed operation kind.</returns>
    public static VaultOp ParseVaultOp(string value)
        => value switch
        {
            "append" => VaultOp.Append,
            "edit_section" => VaultOp.EditSection,
            "edit_key" => VaultOp.EditKey,
            "restore" => VaultOp.Restore,
            _ => VaultOp.Save,
        };
}