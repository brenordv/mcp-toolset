using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.FileVault.Models;

/// <summary>Result of <c>vault_set_meta</c>.</summary>
public sealed record SetMetaResult
{
    /// <summary>Always <c>true</c> on success.</summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    /// <summary>The file's current version (a metadata update does not change it).</summary>
    [JsonPropertyName("current_version")]
    public int CurrentVersion { get; init; }
}