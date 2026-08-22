using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;
using RaccoonNinja.McpToolset.Cli.SkillUsage.Ingest;
using RaccoonNinja.McpToolset.Common.SkillStats.Configuration;
using RaccoonNinja.McpToolset.Common.SkillStats.Storage;

namespace RaccoonNinja.McpToolset.Cli.SkillUsage.Tests.TestSupport;

/// <summary>Drives <see cref="IngestRunner"/> in-process against a fresh temp home.</summary>
internal sealed class IngestHarness : IDisposable
{
    public IngestHarness()
    {
        Home = Path.Combine(Path.GetTempPath(), "skillusage-tests", Guid.NewGuid().ToString("N"));
        Config = SkillStatsConfig.WithHome(Home, homeOverridden: false);
    }

    public string Home { get; }

    public SkillStatsConfig Config { get; }

    public (int ExitCode, string Stderr) Run(string payload, Func<SkillStatsConfig> loadConfig = null)
        => RunBytes(Encoding.UTF8.GetBytes(payload), loadConfig);

    public (int ExitCode, string Stderr) RunBytes(byte[] payload, Func<SkillStatsConfig> loadConfig = null)
    {
        using var stdin = new MemoryStream(payload);
        using var stderr = new StringWriter();
        var exitCode = IngestRunner.Run(stdin, stderr, loadConfig ?? (() => Config));
        return (exitCode, stderr.ToString());
    }

    public IReadOnlyList<SkillUsageRecord> Rows()
    {
        if (!File.Exists(Config.DbPath))
        {
            return [];
        }

        using var connection = new SqliteConnectionFactory(Config).OpenReader();
        return [.. connection.Query<SkillUsageRecord>(
            "SELECT used_at AS UsedAt, skill AS Skill, args AS Args, session_id AS SessionId, cwd AS Cwd "
            + "FROM skill_usage ORDER BY id")];
    }

    public string ReadErrorLog()
        => File.Exists(Config.ErrorLogPath) ? File.ReadAllText(Config.ErrorLogPath) : string.Empty;

    public bool ErrorLogExists() => File.Exists(Config.ErrorLogPath);

    public bool RotatedLogExists() => File.Exists(Path.ChangeExtension(Config.ErrorLogPath, ".old"));

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