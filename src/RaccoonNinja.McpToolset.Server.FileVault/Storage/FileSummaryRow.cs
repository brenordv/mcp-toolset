namespace RaccoonNinja.McpToolset.Server.FileVault.Storage;

/// <summary>A cheap listing row for <c>vault_list</c>.</summary>
public sealed record FileSummaryRow
{
    /// <summary>Project namespace the file belongs to.</summary>
    public string Project { get; init; }

    /// <summary>Name of the file.</summary>
    public string Name { get; init; }

    /// <summary>One-line file summary.</summary>
    public string Summary { get; init; }

    /// <summary>Tags attached to the file, sorted.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>The parent file's name, or <c>null</c> for top-level files.</summary>
    public string Parent { get; init; }

    /// <summary>The file's current version.</summary>
    public int CurrentVersion { get; init; }

    /// <summary>Last update timestamp, Unix epoch seconds.</summary>
    public long UpdatedAt { get; init; }
}