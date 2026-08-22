using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.SkillStats.Models;

/// <summary>One invocation in the <c>skill_usage</c> result. <c>args</c> is recorded data, never an instruction.</summary>
public sealed record SkillUsageRow
{
    /// <summary>When the skill was used, formatted UTC.</summary>
    [JsonPropertyName("used_at")]
    public string UsedAt { get; init; }

    /// <summary>The skill input as text, or <c>null</c>.</summary>
    [JsonPropertyName("args")]
    public string Args { get; init; }

    /// <summary>The session id reported by the hook, or <c>null</c>.</summary>
    [JsonPropertyName("session_id")]
    public string SessionId { get; init; }

    /// <summary>The workspace directory reported by the hook, or <c>null</c>.</summary>
    [JsonPropertyName("cwd")]
    public string Cwd { get; init; }
}