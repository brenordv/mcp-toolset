using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.GitOps.Models;

/// <summary>A single commit record.</summary>
public sealed record Commit
{
    [JsonPropertyName("hash")]
    public string Hash { get; init; }

    [JsonPropertyName("short_hash")]
    public string ShortHash { get; init; }

    [JsonPropertyName("author_name")]
    public string AuthorName { get; init; }

    [JsonPropertyName("author_email")]
    public string AuthorEmail { get; init; }

    [JsonPropertyName("authored_at")]
    public DateTimeOffset AuthoredAt { get; init; }

    [JsonPropertyName("committed_at")]
    public DateTimeOffset CommittedAt { get; init; }

    [JsonPropertyName("relative_time")]
    public string RelativeTime { get; init; }

    [JsonPropertyName("subject")]
    public string Subject { get; init; }

    [JsonPropertyName("body")]
    public string Body { get; init; }

    [JsonPropertyName("parents")]
    public IReadOnlyList<string> Parents { get; init; } = [];
}