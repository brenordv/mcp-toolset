using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.GitOps.Models;

/// <summary>A single match returned by <c>git_grep</c>.</summary>
public sealed record GrepMatch
{
    [JsonPropertyName("path")]
    public string Path { get; init; }

    [JsonPropertyName("line")]
    public int Line { get; init; }

    [JsonPropertyName("content")]
    public string Content { get; init; }
}