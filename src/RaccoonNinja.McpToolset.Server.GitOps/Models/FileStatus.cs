using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.GitOps.Models;

/// <summary>A single working-tree entry.</summary>
public sealed record FileStatus
{
    [JsonPropertyName("path")]
    public string Path { get; init; }

    [JsonPropertyName("staged_status")]
    public string StagedStatus { get; init; }

    [JsonPropertyName("unstaged_status")]
    public string UnstagedStatus { get; init; }

    [JsonPropertyName("is_untracked")]
    public bool IsUntracked { get; init; }

    [JsonPropertyName("is_renamed")]
    public bool IsRenamed { get; init; }

    [JsonPropertyName("orig_path")]
    public string OrigPath { get; init; }
}