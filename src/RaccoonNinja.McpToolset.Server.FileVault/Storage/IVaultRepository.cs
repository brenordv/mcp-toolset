using RaccoonNinja.McpToolset.Server.FileVault.Errors;

namespace RaccoonNinja.McpToolset.Server.FileVault.Storage;

/// <summary>The persistence contract the domain service depends on.</summary>
public interface IVaultRepository
{
    /// <summary>Create a brand-new file at version 1. A pre-existing (project, name) is a conflict.</summary>
    /// <param name="file">The new file's metadata; its snapshot is already on disk.</param>
    /// <returns>The committed version and hash.</returns>
    /// <exception cref="VaultException">Thrown with <see cref="VaultErrorCode.Conflict"/> on a duplicate.</exception>
    Committed CreateFirst(NewFile file);

    /// <summary>Commit the next version of an existing file under optimistic concurrency.</summary>
    /// <param name="write">The write; its snapshot is already on disk.</param>
    /// <returns>The committed version and hash.</returns>
    /// <exception cref="VaultException">
    /// Thrown with <see cref="VaultErrorCode.NotFound"/>, <see cref="VaultErrorCode.Archived"/>
    /// (checked before the version gate), or <see cref="VaultErrorCode.Conflict"/>.
    /// </exception>
    Committed CommitVersion(VersionedWrite write);

    /// <summary>
    /// Update a file's metadata (summary/tags/parent) in place without creating a new version.
    /// </summary>
    /// <param name="update">The metadata change set.</param>
    /// <returns>The (unchanged) current version.</returns>
    int SetMeta(MetaUpdate update);

    /// <summary>List the active children linked directly under (project, name), name-sorted.</summary>
    /// <param name="project">The project namespace.</param>
    /// <param name="name">The parent file name.</param>
    /// <returns>The child rows.</returns>
    IReadOnlyList<ChildRow> Children(string project, string name);

    /// <summary>Fetch the current version's record (metadata + snapshot pointer).</summary>
    /// <param name="project">The project namespace.</param>
    /// <param name="name">The file name.</param>
    /// <returns>The resolved record.</returns>
    FileRecord GetCurrent(string project, string name);

    /// <summary>Fetch a specific historical version's record.</summary>
    /// <param name="project">The project namespace.</param>
    /// <param name="name">The file name.</param>
    /// <param name="version">The version to fetch.</param>
    /// <returns>The resolved record.</returns>
    FileRecord GetVersion(string project, string name, int version);

    /// <summary>List active files matching <paramref name="filter"/>.</summary>
    /// <param name="filter">Project/tags/query filters.</param>
    /// <returns>Matching rows, ordered by <c>updated_at DESC, name ASC</c>.</returns>
    IReadOnlyList<FileSummaryRow> List(ListFilter filter);

    /// <summary>Return the full version history, newest first.</summary>
    /// <param name="project">The project namespace.</param>
    /// <param name="name">The file name.</param>
    /// <returns>The version rows.</returns>
    IReadOnlyList<VersionRow> History(string project, string name);

    /// <summary>Flip a file's lifecycle state (archive/restore). Unconditional; bumps <c>updated_at</c>.</summary>
    /// <param name="project">The project namespace.</param>
    /// <param name="name">The file name.</param>
    /// <param name="state">The target state.</param>
    void SetState(string project, string name, FileState state);

    /// <summary>
    /// Permanently delete a file and return the snapshot paths the caller must unlink, excluding
    /// any path still referenced by another live version row.
    /// </summary>
    /// <param name="project">The project namespace.</param>
    /// <param name="name">The file name.</param>
    /// <returns>The snapshot rel_paths safe to remove or retain-rename.</returns>
    IReadOnlyList<string> Purge(string project, string name);

    /// <summary>
    /// Return the subset of <paramref name="relPaths"/> referenced by any live version row,
    /// compared case-insensitively — the recheck run just before unlinking, so a purge can
    /// never delete a snapshot another file's version still points at.
    /// </summary>
    /// <param name="relPaths">Candidate snapshot paths.</param>
    /// <returns>The still-referenced paths as stored in the database.</returns>
    IReadOnlyList<string> StillReferenced(IReadOnlyList<string> relPaths);
}