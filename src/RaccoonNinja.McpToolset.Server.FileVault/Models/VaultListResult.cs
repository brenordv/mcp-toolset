using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.FileVault.Models;

/// <summary>Result of <c>vault_list</c>.</summary>
public sealed record VaultListResult
{
    /// <summary>The matching files, ordered by <c>updated_at</c> descending.</summary>
    [JsonPropertyName("items")]
    public IReadOnlyList<VaultListItem> Items { get; init; } = [];
}