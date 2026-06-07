using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.GitOps.Models;

/// <summary>Aggregated diff payload returned by <c>git_diff</c>, <c>git_show</c>, and <c>git_stash_show</c>.</summary>
public sealed record DiffResult
{
    [JsonPropertyName("files")]
    public IReadOnlyList<FileDiff> Files { get; init; } = [];

    [JsonPropertyName("total_additions")]
    public int TotalAdditions { get; init; }

    [JsonPropertyName("total_deletions")]
    public int TotalDeletions { get; init; }

    [JsonPropertyName("mode")]
    public string Mode { get; init; } = "worktree";

    [JsonPropertyName("truncated")]
    public bool Truncated { get; init; }
}