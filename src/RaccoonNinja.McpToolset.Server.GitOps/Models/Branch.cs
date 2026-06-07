using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.GitOps.Models;

/// <summary>A local or remote branch entry returned by <c>git_branch_list</c>.</summary>
public sealed record Branch
{
    [JsonPropertyName("name")]
    public string Name { get; init; }

    [JsonPropertyName("is_current")]
    public bool IsCurrent { get; init; }

    [JsonPropertyName("is_remote")]
    public bool IsRemote { get; init; }

    [JsonPropertyName("upstream")]
    public string Upstream { get; init; }

    [JsonPropertyName("tip_hash")]
    public string TipHash { get; init; }

    [JsonPropertyName("subject")]
    public string Subject { get; init; }
}