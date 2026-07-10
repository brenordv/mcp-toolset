namespace RaccoonNinja.McpToolset.Server.FileVault.Storage;

/// <summary>
/// A tri-state change to a file's parent link, used by save (leave/set) and set_meta
/// (leave/clear/set). <see cref="Leave"/> keeps the existing parent untouched; <see cref="Clear"/>
/// detaches the file to top-level; a non-null <see cref="ParentName"/> links it under the named
/// parent (resolved within the same project).
/// </summary>
public sealed record ParentUpdate
{
    private ParentUpdate(bool clear, string parentName)
    {
        IsClear = clear;
        ParentName = parentName;
    }

    /// <summary>Do not change the current parent link.</summary>
    public static ParentUpdate Leave { get; } = new(false, null);

    /// <summary>Detach the file from any parent (make it top-level).</summary>
    public static ParentUpdate Clear { get; } = new(true, null);

    /// <summary><c>true</c> when the link should be removed.</summary>
    public bool IsClear { get; }

    /// <summary>The parent name to link under, or <c>null</c> for leave/clear.</summary>
    public string ParentName { get; }

    /// <summary><c>true</c> when this update sets a new parent.</summary>
    public bool IsSet => ParentName is not null;

    /// <summary><c>true</c> when this update changes nothing.</summary>
    public bool IsLeave => !IsClear && ParentName is null;

    /// <summary>Link the file under the parent with this name (same project).</summary>
    /// <param name="parentName">The validated parent file name.</param>
    /// <returns>The set-parent update.</returns>
    public static ParentUpdate Set(string parentName)
        => new(false, parentName);
}