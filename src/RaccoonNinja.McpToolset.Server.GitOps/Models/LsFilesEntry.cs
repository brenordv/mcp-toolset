using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.GitOps.Models;

/// <summary>One index entry from <c>git ls-files --stage</c>: a tracked path with its mode, blob id, and stage number.</summary>
public sealed record LsFilesEntry
{
    /// <summary>The repo-relative path, verbatim as git stores it.</summary>
    [JsonPropertyName("path")]
    public string Path { get; init; }

    /// <summary>The six octal mode digits as git prints them (<c>100644</c>, <c>100755</c>, <c>120000</c>, <c>160000</c>).</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; init; }

    /// <summary>The object name of the staged content.</summary>
    [JsonPropertyName("object_id")]
    public string ObjectId { get; init; }

    /// <summary>0 for a normal entry; 1 to 3 for the base, ours, and theirs sides of an unmerged entry.</summary>
    [JsonPropertyName("stage")]
    public int Stage { get; init; }
}