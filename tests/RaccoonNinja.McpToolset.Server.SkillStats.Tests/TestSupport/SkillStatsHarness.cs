using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Server.SkillStats.Metrics;
using RaccoonNinja.McpToolset.Server.SkillStats.Tools;
using RaccoonNinja.McpToolset.Common.SkillStats.Configuration;
using RaccoonNinja.McpToolset.Common.SkillStats.Storage;

namespace RaccoonNinja.McpToolset.Server.SkillStats.Tests.TestSupport;

/// <summary>
/// A per-test server harness over a fresh temp store. It wires the three tools to a real repository
/// and <see cref="ToolCommon"/>. Call <see cref="Migrate"/> to create the store, then <see cref="Seed"/>
/// rows; leave it un-migrated to exercise the store-availability gate.
/// </summary>
internal sealed class SkillStatsHarness : IDisposable
{
    public SkillStatsHarness(bool homeOverridden = false)
    {
        Home = Path.Combine(Path.GetTempPath(), "skillstats-server-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Home);
        Config = SkillStatsConfig.WithHome(Home, homeOverridden);
        Factory = new SqliteConnectionFactory(Config);
        Repository = new SkillUsageRepository(Factory);
        var common = new ToolCommon(new SessionMetrics(), NullLoggerFactory.Instance, Config, Factory);
        TopSkills = new TopSkillsTool(common, Repository);
        SkillUsage = new SkillUsageTool(common, Repository);
        UsageSummary = new UsageSummaryTool(common, Repository, Config);
    }

    public string Home { get; }

    public SkillStatsConfig Config { get; }

    public SqliteConnectionFactory Factory { get; }

    public SkillUsageRepository Repository { get; }

    public TopSkillsTool TopSkills { get; }

    public SkillUsageTool SkillUsage { get; }

    public UsageSummaryTool UsageSummary { get; }

    public void Migrate() => Migrator.Run(Factory);

    public void Seed(string skill, long usedAt, string args = null, string sessionId = null, string cwd = null)
        => Repository.Insert(new SkillUsageRecord { UsedAt = usedAt, Skill = skill, Args = args, SessionId = sessionId, Cwd = cwd });

    public void SetSchemaVersion(long version)
    {
        using var connection = Factory.OpenWriter();
        connection.Execute(
            "UPDATE meta SET value = @value WHERE key = 'schema_version'",
            new { value = version.ToString(CultureInfo.InvariantCulture) });
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Home, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort temp cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort temp cleanup.
        }
    }
}