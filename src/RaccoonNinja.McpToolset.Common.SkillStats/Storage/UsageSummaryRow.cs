namespace RaccoonNinja.McpToolset.Common.SkillStats.Storage;

/// <summary>Store-wide totals for the <c>usage_summary</c> tool.</summary>
public sealed record UsageSummaryRow
{
    /// <summary>Total recorded invocations across all skills.</summary>
    public long TotalRecords { get; init; }

    /// <summary>How many distinct skills have been recorded.</summary>
    public long DistinctSkills { get; init; }

    /// <summary>The earliest recorded use, Unix epoch seconds (UTC), or <c>null</c> when the store is empty.</summary>
    public long? FirstUsed { get; init; }

    /// <summary>The latest recorded use, Unix epoch seconds (UTC), or <c>null</c> when the store is empty.</summary>
    public long? LastUsed { get; init; }
}