using Microsoft.Data.Sqlite;
using RaccoonNinja.McpToolset.Shared.SkillStats.Configuration;
using RaccoonNinja.McpToolset.Shared.SkillStats.Storage;

namespace RaccoonNinja.McpToolset.Shared.SkillStats.Tests.TestSupport;

/// <summary>A per-test temp store: a fresh home directory, a config rooted at it, and a connection factory.</summary>
internal sealed class TempStore : IDisposable
{
    public TempStore()
    {
        Home = Path.Combine(Path.GetTempPath(), "skillstats-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Home);
        Config = SkillStatsConfig.WithHome(Home, homeOverridden: false);
        Factory = new SqliteConnectionFactory(Config);
    }

    public string Home { get; }

    public SkillStatsConfig Config { get; }

    public SqliteConnectionFactory Factory { get; }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Home, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort temp cleanup; the OS temp sweeper owns the rest.
        }
        catch (UnauthorizedAccessException)
        {
            // Windows reports a lingering -shm handle this way.
        }
    }
}