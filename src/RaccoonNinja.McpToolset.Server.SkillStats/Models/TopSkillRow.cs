using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.SkillStats.Models;

/// <summary>One row of the <c>top_skills</c> result: a skill and its use count and activity window.</summary>
public sealed record TopSkillRow
{
    /// <summary>The normalized skill name.</summary>
    [JsonPropertyName("skill")]
    public string Skill { get; init; }

    /// <summary>How many times the skill was used in the window.</summary>
    [JsonPropertyName("uses")]
    public long Uses { get; init; }

    /// <summary>The earliest use in the window, formatted UTC.</summary>
    [JsonPropertyName("first_used")]
    public string FirstUsed { get; init; }

    /// <summary>The latest use in the window, formatted UTC.</summary>
    [JsonPropertyName("last_used")]
    public string LastUsed { get; init; }
}