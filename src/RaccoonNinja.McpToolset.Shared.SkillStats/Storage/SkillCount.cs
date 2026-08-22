namespace RaccoonNinja.McpToolset.Shared.SkillStats.Storage;

/// <summary>An aggregate usage count for one skill, for the <c>top_skills</c> tool.</summary>
public sealed record SkillCount
{
    /// <summary>The normalized skill name.</summary>
    public string Skill { get; init; }

    /// <summary>How many times the skill was used in the window.</summary>
    public long Uses { get; init; }

    /// <summary>The earliest use in the window, Unix epoch seconds (UTC).</summary>
    public long FirstUsed { get; init; }

    /// <summary>The latest use in the window, Unix epoch seconds (UTC).</summary>
    public long LastUsed { get; init; }
}