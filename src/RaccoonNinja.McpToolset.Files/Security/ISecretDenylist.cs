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

    /// <summary>
    /// The denylist patterns as display strings, for surfacing in a server's scope description. It reveals
    /// only the rule set (built-in directory segments and file globs, plus any operator extensions) and
    /// never probes the filesystem, so it discloses nothing about what a given tree actually contains.
    /// </summary>
    /// <returns>The denied directory segments (rendered as <c>segment/**</c>) and the file-name globs.</returns>
    IReadOnlyCollection<string> DescribePatterns();

    /// <summary>
    /// The leaf directory segments a confinement root must not be placed on, because they are the
    /// non-final segment of a multi-segment denylist marker (for example <c>.config</c>, the parent of
    /// <c>.config/gcloud</c>). Rooting a walk on such a segment would shed the parent context the marker
    /// needs, so a server refuses a base root or a per-call scope whose leaf is one of these.
    /// </summary>
    /// <returns>The reparent-unsafe leaf segments (empty when no multi-segment marker exists).</returns>
    IReadOnlyCollection<string> ReparentUnsafeLeafSegments { get; }
}