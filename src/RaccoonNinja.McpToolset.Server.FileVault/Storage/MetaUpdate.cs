namespace RaccoonNinja.McpToolset.Server.FileVault.Storage;

/// <summary>
/// A metadata-only update: changes summary/tags/parent in place without creating a new content
/// version or touching any snapshot.
/// </summary>
public sealed record MetaUpdate
{
    /// <summary>Project the target file lives in.</summary>
    public string Project { get; init; }

    /// <summary>Name of the target file.</summary>
    public string Name { get; init; }

    /// <summary>Non-null replaces the file summary; <c>null</c> leaves it unchanged.</summary>
    public string Summary { get; init; }

    /// <summary>Non-null replaces the tag set; <c>null</c> leaves the existing tags untouched.</summary>
    public IReadOnlyList<string> Tags { get; init; }

    /// <summary>Tri-state change to the parent link.</summary>
    public ParentUpdate Parent { get; init; } = ParentUpdate.Leave;

    /// <summary>
    /// Optional optimistic-concurrency guard: when set, the update is rejected as a conflict
    /// unless it equals the file's current version. The version is not bumped either way.
    /// </summary>
    public int? BaseVersion { get; init; }
}