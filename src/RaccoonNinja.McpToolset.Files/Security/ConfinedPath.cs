namespace RaccoonNinja.McpToolset.Files.Security;

/// <summary>
/// A path that has been confined to a root: fully resolved through every symbolic link and junction,
/// verified to sit inside the root, and expressed both as the real absolute path (for opening) and as
/// the root-relative POSIX path (for output, denylist matching, and glob matching).
/// </summary>
public sealed record ConfinedPath
{
    /// <summary>
    /// The resolved absolute path on disk, with every reparse point in the chain collapsed and, on
    /// Windows, expanded to its long form. Use it to open the file so the open lands on the same target
    /// that was verified. It is machine-identifying and must never be written into output or any
    /// persisted artifact; only <see cref="RelativePath"/> is safe to surface.
    /// </summary>
    public string RealPath { get; init; }

    /// <summary>
    /// The <c>/</c>-separated path relative to the root (<c>.</c> for the root itself). This is the only
    /// form that leaves the confiner: it carries no absolute location and is what the denylist, the glob
    /// matcher, and tool responses consume.
    /// </summary>
    public string RelativePath { get; init; }

    /// <summary>Whether the resolved target currently exists; <c>false</c> for a not-yet-created leaf whose parent was resolved instead.</summary>
    public bool Exists { get; init; }
}