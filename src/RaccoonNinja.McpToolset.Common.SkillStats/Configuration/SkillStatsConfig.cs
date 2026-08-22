using RaccoonNinja.McpToolset.Common.SkillStats.Platform;

namespace RaccoonNinja.McpToolset.Common.SkillStats.Configuration;

/// <summary>
/// Resolved runtime configuration for the skill-usage store. The store is a single global directory,
/// defaulting to <c>~/.skill-stats</c> and overridable via <c>SKILL_STATS_HOME</c>. Both binaries
/// resolve the same layout so the ingest CLI and the read-only server point at one store.
/// </summary>
public sealed record SkillStatsConfig
{
    /// <summary>SQLite busy timeout, in milliseconds. A locked write waits this long before failing.</summary>
    public const int BusyTimeoutMs = 5_000;

    private const string HomeEnvVar = "SKILL_STATS_HOME";
    private const string HomeDirectoryName = ".skill-stats";
    private const string DatabaseFileName = "skill-stats.db";
    private const string ErrorLogFileName = "ingest-errors.log";

    /// <summary>The store root (<c>~/.skill-stats</c> or <c>$SKILL_STATS_HOME</c>).</summary>
    public string Home { get; init; }

    /// <summary>The SQLite database file (<c>&lt;home&gt;/skill-stats.db</c>).</summary>
    public string DbPath { get; init; }

    /// <summary>The ingest error log (<c>&lt;home&gt;/ingest-errors.log</c>).</summary>
    public string ErrorLogPath { get; init; }

    /// <summary>Whether <c>SKILL_STATS_HOME</c> was set. Surfaced as a privacy-safe boolean by the server.</summary>
    public bool HomeOverridden { get; init; }

    /// <summary>Resolve configuration from the environment.</summary>
    /// <returns>The resolved configuration.</returns>
    /// <exception cref="SkillStatsStartupException">Thrown when no home directory can be determined.</exception>
    public static SkillStatsConfig Load()
    {
        var (home, overridden) = ResolveHome();
        return WithHome(home, overridden);
    }

    /// <summary>Build a configuration rooted at an explicit home.</summary>
    /// <param name="home">The store root directory.</param>
    /// <param name="homeOverridden">Whether the home came from <c>SKILL_STATS_HOME</c>.</param>
    /// <returns>The resolved configuration.</returns>
    public static SkillStatsConfig WithHome(string home, bool homeOverridden)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        return new SkillStatsConfig
        {
            Home = home,
            DbPath = Path.Combine(home, DatabaseFileName),
            ErrorLogPath = Path.Combine(home, ErrorLogFileName),
            HomeOverridden = homeOverridden,
        };
    }

    /// <summary>Create the store directory if missing, then restrict its permissions (0700 on Unix).</summary>
    public void EnsureDirs()
    {
        Directory.CreateDirectory(Home);
        StoreHardening.RestrictPermissions(Home);
    }

    private static (string Home, bool Overridden) ResolveHome()
    {
        var explicitHome = Environment.GetEnvironmentVariable(HomeEnvVar);
        if (!string.IsNullOrWhiteSpace(explicitHome))
        {
            return (explicitHome, true);
        }

        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(userHome)
            ? throw new SkillStatsStartupException($"could not determine the user home directory; set {HomeEnvVar}")
            : (Path.Combine(userHome, HomeDirectoryName), false);
    }
}