namespace RaccoonNinja.McpToolset.Server.FileVault.Storage;

/// <summary>
/// Filter for <see cref="IVaultRepository.List"/>. Empty tags and a null query match everything
/// active.
/// </summary>
public sealed record ListFilter
{
    /// <summary>Project the files must belong to; <c>null</c> lists across all projects.</summary>
    public string Project { get; init; }

    /// <summary>Files must carry all of these tags to match.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>FTS query over name/summary/tags; <c>null</c> means no keyword filter.</summary>
    public string Query { get; init; }
}