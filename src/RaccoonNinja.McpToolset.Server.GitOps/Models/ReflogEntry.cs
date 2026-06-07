using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.GitOps.Models;

/// <summary>A single reflog entry returned by <c>git_reflog</c>.</summary>
public sealed record ReflogEntry
{
    [JsonPropertyName("hash")]
    public string Hash { get; init; }

    [JsonPropertyName("short_hash")]
    public string ShortHash { get; init; }

    [JsonPropertyName("selector")]
    public string Selector { get; init; }

    [JsonPropertyName("subject")]
    public string Subject { get; init; }

    [JsonPropertyName("when")]
    public DateTimeOffset When { get; init; }

    [JsonPropertyName("relative_time")]
    public string RelativeTime { get; init; }
}