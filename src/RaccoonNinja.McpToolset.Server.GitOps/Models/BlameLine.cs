using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.GitOps.Models;

/// <summary>A single blame entry returned by <c>git_blame</c>.</summary>
public sealed record BlameLine
{
    [JsonPropertyName("line_no")]
    public int LineNo { get; init; }

    [JsonPropertyName("content")]
    public string Content { get; init; }

    [JsonPropertyName("commit_hash")]
    public string CommitHash { get; init; }

    [JsonPropertyName("author_name")]
    public string AuthorName { get; init; }

    [JsonPropertyName("authored_at")]
    public DateTimeOffset AuthoredAt { get; init; }

    [JsonPropertyName("relative_time")]
    public string RelativeTime { get; init; }
}