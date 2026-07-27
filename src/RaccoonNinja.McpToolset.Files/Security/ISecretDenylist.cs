namespace RaccoonNinja.McpToolset.Files.Security;

/// <summary>
/// The non-overridable secret denylist shared by both servers. It is the one rule that survives every
/// flag, because once the read tools are blanket-approved nobody reviews the calls: a denylisted file
/// is never read into model context and never written. Matching is always case-insensitive and is
/// applied to the resolved, root-relative path so a symlink cannot smuggle a secret past its name.
/// </summary>
public interface ISecretDenylist
{
    /// <summary>Whether a file at <paramref name="relativePath"/> is denied for both reading and writing.</summary>
    /// <param name="relativePath">The <c>/</c>-separated, root-relative path of the file.</param>
    /// <returns><c>true</c> when the file is a known secret or sits inside a denied directory.</returns>
    /// <exception cref="System.ArgumentException">Thrown when <paramref name="relativePath"/> is null or blank.</exception>
    bool IsDeniedFile(string relativePath);

    /// <summary>Whether a directory at <paramref name="relativePath"/> must be pruned from traversal entirely.</summary>
    /// <param name="relativePath">The <c>/</c>-separated, root-relative path of the directory.</param>
    /// <returns><c>true</c> when the directory (for example <c>.git</c> or <c>.ssh</c>) is off-limits.</returns>
    /// <exception cref="System.ArgumentException">Thrown when <paramref name="relativePath"/> is null or blank.</exception>
    bool IsDeniedDirectory(string relativePath);
}