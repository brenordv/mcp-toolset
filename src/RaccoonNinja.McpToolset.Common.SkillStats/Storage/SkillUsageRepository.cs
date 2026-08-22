using Dapper;

namespace RaccoonNinja.McpToolset.Common.SkillStats.Storage;

/// <summary>
/// The skill-usage repository, on Dapper over Microsoft.Data.Sqlite. Every value binds as a
/// <c>@parameter</c>; nothing is interpolated into SQL text. The ingest CLI calls <see cref="Insert"/>
/// on a writer connection; the read-only server calls the query methods on reader connections.
/// </summary>
public sealed class SkillUsageRepository(SqliteConnectionFactory factory)
{
    /// <summary>The maximum stored length of <c>args</c>, in chars; longer input is truncated on insert.</summary>
    public const int MaxArgsChars = 8192;

    /// <summary>Append one usage row, truncating <c>args</c> to <see cref="MaxArgsChars"/>.</summary>
    /// <param name="record">The row to insert.</param>
    public void Insert(SkillUsageRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var connection = factory.OpenWriter();
        connection.Execute(
            "INSERT INTO skill_usage(used_at, skill, args, session_id, cwd) "
            + "VALUES(@UsedAt, @Skill, @Args, @SessionId, @Cwd)",
            new
            {
                record.UsedAt,
                record.Skill,
                Args = Truncate(record.Args),
                record.SessionId,
                record.Cwd,
            });
    }

    /// <summary>Return the most-used skills, ordered by use count then name for deterministic ties.</summary>
    /// <param name="cutoffEpoch">Inclusive lower bound on <c>used_at</c> (Unix seconds), or <c>null</c> for all time.</param>
    /// <param name="limit">The maximum number of skills to return.</param>
    /// <returns>The skill counts, newest activity first within equal counts by name.</returns>
    public IReadOnlyList<SkillCount> TopSkills(long? cutoffEpoch, int limit)
    {
        using var connection = factory.OpenReader();
        const string projection =
            "SELECT skill AS Skill, COUNT(*) AS Uses, MIN(used_at) AS FirstUsed, MAX(used_at) AS LastUsed "
            + "FROM skill_usage ";
        const string tail = "GROUP BY skill ORDER BY COUNT(*) DESC, skill ASC LIMIT @limit";
        return cutoffEpoch is long cutoff
            ? [.. connection.Query<SkillCount>(projection + "WHERE used_at >= @cutoff " + tail, new { cutoff, limit })]
            : [.. connection.Query<SkillCount>(projection + tail, new { limit })];
    }

    /// <summary>Return the newest invocations of one skill.</summary>
    /// <param name="skill">The normalized skill name to look up.</param>
    /// <param name="cutoffEpoch">Inclusive lower bound on <c>used_at</c> (Unix seconds), or <c>null</c> for all time.</param>
    /// <param name="limit">The maximum number of rows to return.</param>
    /// <returns>The matching rows, newest first; empty when the skill is unknown.</returns>
    public IReadOnlyList<SkillUsageRecord> UsageForSkill(string skill, long? cutoffEpoch, int limit)
    {
        using var connection = factory.OpenReader();
        const string projection =
            "SELECT used_at AS UsedAt, skill AS Skill, args AS Args, session_id AS SessionId, cwd AS Cwd "
            + "FROM skill_usage WHERE skill = @skill ";
        const string tail = "ORDER BY used_at DESC, id DESC LIMIT @limit";
        return cutoffEpoch is long cutoff
            ? [.. connection.Query<SkillUsageRecord>(projection + "AND used_at >= @cutoff " + tail, new { skill, cutoff, limit })]
            : [.. connection.Query<SkillUsageRecord>(projection + tail, new { skill, limit })];
    }

    /// <summary>Return store-wide totals and the first/last recorded use.</summary>
    /// <returns>The summary row; counts are 0 and the timestamps <c>null</c> when the store is empty.</returns>
    public UsageSummaryRow Summary()
    {
        using var connection = factory.OpenReader();
        return connection.QuerySingle<UsageSummaryRow>(
            "SELECT COUNT(*) AS TotalRecords, COUNT(DISTINCT skill) AS DistinctSkills, "
            + "MIN(used_at) AS FirstUsed, MAX(used_at) AS LastUsed FROM skill_usage");
    }

    /// <summary>Read the store's schema version through a read-only connection.</summary>
    /// <returns>The schema version, or 0 when the meta table is absent.</returns>
    public long SchemaVersion()
    {
        using var connection = factory.OpenReader();
        return Migrator.CurrentVersion(connection);
    }

    private static string Truncate(string args)
        => args is { Length: > MaxArgsChars } ? args[..MaxArgsChars] : args;
}