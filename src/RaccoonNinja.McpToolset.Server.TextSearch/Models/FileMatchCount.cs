using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Models;

/// <summary>One entry returned by <c>search_text</c> in count mode: a scope-relative path and its number of matching lines.</summary>
public sealed record FileMatchCount : IHasPath
{
    /// <summary>The <c>/</c>-separated path relative to the call's scope.</summary>
    [JsonPropertyName("path")]
    public string Path { get; init; }

    /// <summary>The number of lines in the file that match the pattern.</summary>
    [JsonPropertyName("match_count")]
    public int MatchCount { get; init; }
}