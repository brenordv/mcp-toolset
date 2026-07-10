using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.FileVault.Models;

/// <summary>Result of <c>vault_history</c>.</summary>
public sealed record HistoryResult
{
    /// <summary>The version rows, newest first.</summary>
    [JsonPropertyName("versions")]
    public IReadOnlyList<HistoryItem> Versions { get; init; } = [];
}