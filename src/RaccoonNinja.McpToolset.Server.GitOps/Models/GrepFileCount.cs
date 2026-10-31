using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.GitOps.Models;

/// <summary>One file's count of matching lines, returned by <c>git_grep</c> in count mode.</summary>
public sealed record GrepFileCount
{
    /// <summary>The repo-relative path.</summary>
    [JsonPropertyName("path")]
    public string Path { get; init; }

    /// <summary>The number of lines that match.</summary>
    [JsonPropertyName("match_count")]
    public int MatchCount { get; init; }
}