using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Models;

/// <summary>One entry returned by <c>find_files</c>: a scope-relative path with basic metadata.</summary>
public sealed record FileHit : IHasPath
{
    /// <summary>The <c>/</c>-separated path relative to the call's scope (the <c>cwd</c>, or the base root when it is omitted).</summary>
    [JsonPropertyName("path")]
    public string Path { get; init; }

    /// <summary>The file size in bytes.</summary>
    [JsonPropertyName("size")]
    public long Size { get; init; }

    /// <summary>The last-written time, ISO-8601 UTC.</summary>
    [JsonPropertyName("last_modified")]
    public string LastModified { get; init; }
}