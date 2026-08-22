namespace RaccoonNinja.McpToolset.Shared.SkillStats.Storage;

/// <summary>One recorded Skill tool invocation: the row the ingest CLI writes and the server reads back.</summary>
public sealed record SkillUsageRecord
{
    /// <summary>When the skill was used, Unix epoch seconds (UTC).</summary>
    public long UsedAt { get; init; }

    /// <summary>The normalized skill name.</summary>
    public string Skill { get; init; }

    /// <summary>The skill input as text, or <c>null</c>. Truncated to 8192 chars on insert.</summary>
    public string Args { get; init; }

    /// <summary>The session id reported by the hook, or <c>null</c>.</summary>
    public string SessionId { get; init; }

    /// <summary>The workspace directory reported by the hook, or <c>null</c>.</summary>
    public string Cwd { get; init; }
}