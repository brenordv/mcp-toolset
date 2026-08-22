namespace RaccoonNinja.McpToolset.Shared.SkillStats.Configuration;

/// <summary>
/// A fatal setup failure in the skill-usage store: an unresolvable home directory or a failed
/// migration. The ingest CLI treats it as a config-stage failure; the read-only server logs it and
/// keeps serving so a store created later starts working without a restart.
/// </summary>
public sealed class SkillStatsStartupException : Exception
{
    /// <summary>Create the exception with a human-readable reason.</summary>
    /// <param name="message">What made setup impossible.</param>
    public SkillStatsStartupException(string message)
        : base(message)
    {
    }

    /// <summary>Create the exception wrapping an underlying failure.</summary>
    /// <param name="message">What made setup impossible.</param>
    /// <param name="innerException">The underlying failure.</param>
    public SkillStatsStartupException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}