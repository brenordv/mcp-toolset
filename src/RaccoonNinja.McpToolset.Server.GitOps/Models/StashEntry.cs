using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.GitOps.Models;

/// <summary>A stash entry returned by <c>git_stash_list</c>.</summary>
public sealed record StashEntry
{
    [JsonPropertyName("index")]
    public int Index { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; }

    [JsonPropertyName("subject")]
    public string Subject { get; init; }

    [JsonPropertyName("relative_time")]
    public string RelativeTime { get; init; }

    [JsonPropertyName("branch")]
    public string Branch { get; init; }
}