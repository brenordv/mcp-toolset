namespace RaccoonNinja.McpToolset.Server.FileVault.Storage;

/// <summary>Database string conversions for <see cref="FileState"/>.</summary>
public static class FileStateExtensions
{
    /// <summary>The lowercase string persisted in <c>files.state</c>.</summary>
    /// <param name="state">The lifecycle state.</param>
    /// <returns>The database string.</returns>
    public static string ToDbString(this FileState state)
        => state == FileState.Archived ? "archived" : "active";

    /// <summary>Parse a stored state string; unknown values map to <see cref="FileState.Active"/>.</summary>
    /// <param name="value">The stored string.</param>
    /// <returns>The parsed state.</returns>
    public static FileState ParseFileState(string value)
        => value == "archived" ? FileState.Archived : FileState.Active;
}