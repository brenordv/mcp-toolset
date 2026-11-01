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

    /// <summary>The author recorded in the line's commit, the name <c>git_log</c> and <c>git_show</c> report.</summary>
    [JsonPropertyName("author_name")]
    public string AuthorName { get; init; }

    /// <summary>
    /// The name the repository's mailmap shows for the line's author, when it differs from <see cref="AuthorName"/>;
    /// <c>null</c> when the mailmap leaves the name as recorded, and for a line not committed yet.
    /// </summary>
    [JsonPropertyName("mailmap_author_name")]
    public string MailmapAuthorName { get; init; }

    [JsonPropertyName("authored_at")]
    public DateTimeOffset AuthoredAt { get; init; }

    [JsonPropertyName("relative_time")]
    public string RelativeTime { get; init; }
}