using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.FileVault.Models;

/// <summary>Result of a lifecycle action (<c>archive</c>/<c>restore</c>/<c>purge</c>).</summary>
public sealed record StatusResult
{
    /// <summary>Always <c>true</c> on success.</summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    /// <summary>A short action confirmation: <c>archived</c>, <c>restored</c>, or <c>purged</c>.</summary>
    [JsonPropertyName("message")]
    public string Message { get; init; }
}