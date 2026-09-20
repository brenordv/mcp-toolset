namespace RaccoonNinja.McpToolset.Server.FileVault.Storage;

/// <summary>
/// A row for <c>vault_search</c>: an active file plus the <c>rel_path</c> of its current-version
/// snapshot, so the body can be read on demand. Tags are omitted deliberately; search is
/// body-only and nothing consumes them.
/// </summary>
public sealed record SearchCandidateRow
{
    /// <summary>Project namespace the file belongs to.</summary>
    public string Project { get; init; }

    /// <summary>Name of the file.</summary>
    public string Name { get; init; }

    /// <summary>One-line file summary.</summary>
    public string Summary { get; init; }

    /// <summary>The file's current version.</summary>
    public int CurrentVersion { get; init; }

    /// <summary>Last update timestamp, Unix epoch seconds.</summary>
    public long UpdatedAt { get; init; }

    /// <summary>The <c>/</c>-separated snapshot path of the current version.</summary>
    public string RelPath { get; init; }
}