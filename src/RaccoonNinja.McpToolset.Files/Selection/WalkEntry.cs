namespace RaccoonNinja.McpToolset.Files.Selection;

/// <summary>
/// One filesystem entry that survived the walk's pruning (confinement, denylist, ignore, and
/// symlink skipping). Its path is always the <c>/</c>-separated root-relative form, never an absolute
/// path, so it is safe to surface in output.
/// </summary>
public sealed record WalkEntry
{
    /// <summary>The <c>/</c>-separated path relative to the walk root.</summary>
    public string RelativePath { get; init; }

    /// <summary>Whether this entry is a directory.</summary>
    public bool IsDirectory { get; init; }

    /// <summary>The file size in bytes; <c>0</c> for a directory.</summary>
    public long Size { get; init; }

    /// <summary>The last-write time in UTC.</summary>
    public DateTimeOffset LastModifiedUtc { get; init; }
}