using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.GitOps.Models;

/// <summary>Output of the <c>git_status</c> tool.</summary>
public sealed record StatusResult
{
    [JsonPropertyName("branch")]
    public string Branch { get; init; }

    [JsonPropertyName("upstream")]
    public string Upstream { get; init; }

    [JsonPropertyName("ahead")]
    public int Ahead { get; init; }

    [JsonPropertyName("behind")]
    public int Behind { get; init; }

    [JsonPropertyName("staged")]
    public IReadOnlyList<FileStatus> Staged { get; init; } = [];

    [JsonPropertyName("unstaged")]
    public IReadOnlyList<FileStatus> Unstaged { get; init; } = [];

    [JsonPropertyName("untracked")]
    public IReadOnlyList<FileStatus> Untracked { get; init; } = [];

    [JsonPropertyName("is_clean")]
    public bool IsClean { get; init; }
}