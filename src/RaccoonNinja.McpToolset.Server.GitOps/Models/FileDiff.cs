using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.GitOps.Models;

/// <summary>A per-file diff entry inside a <see cref="DiffResult"/>.</summary>
public sealed record FileDiff
{
    [JsonPropertyName("path")]
    public string Path { get; init; }

    [JsonPropertyName("change_type")]
    public ChangeType ChangeType { get; init; } = ChangeType.Modified;

    [JsonPropertyName("old_path")]
    public string OldPath { get; init; }

    [JsonPropertyName("additions")]
    public int Additions { get; init; }

    [JsonPropertyName("deletions")]
    public int Deletions { get; init; }

    [JsonPropertyName("is_binary")]
    public bool IsBinary { get; init; }

    [JsonPropertyName("hunks")]
    public IReadOnlyList<DiffHunk> Hunks { get; init; } = [];
}