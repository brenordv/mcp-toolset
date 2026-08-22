using System.Globalization;
using System.Reflection;
using Dapper;
using Microsoft.Data.Sqlite;
using RaccoonNinja.McpToolset.Shared.SkillStats.Configuration;

namespace RaccoonNinja.McpToolset.Shared.SkillStats.Storage;

/// <summary>
/// Forward-only schema migrations driven by <c>meta(schema_version)</c>. Migrations are embedded at
/// build time and applied in order. It is race-tolerant for two first-ever ingests in parallel: each
/// migration runs inside a write-up-front (non-deferred) transaction and re-reads the version inside
/// it, so the loser blocks on the write lock, then sees the winner's schema and applies nothing.
/// </summary>
public static class Migrator
{
    private static readonly (long Version, string ResourceName)[] Migrations =
    [
        (1, "RaccoonNinja.McpToolset.Shared.SkillStats.Storage.Schema.0001_init.sql"),
    ];

    /// <summary>The highest schema version this build knows how to produce.</summary>
    public const long LatestVersion = 1;

    /// <summary>Apply every pending migration and return the number applied.</summary>
    /// <param name="factory">The connection factory for the target database.</param>
    /// <returns>The count of migrations that ran.</returns>
    /// <exception cref="SkillStatsStartupException">Thrown when a migration statement fails.</exception>
    public static int Run(SqliteConnectionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        using var connection = factory.OpenWriter();
        var applied = 0;

        foreach (var (version, resourceName) in Migrations)
        {
            // A non-deferred transaction takes the write lock up front, so a parallel migrator blocks
            // here and re-reads the version below once it wins the lock.
            using var transaction = connection.BeginTransaction();
            if (version <= CurrentVersion(connection, transaction))
            {
                transaction.Rollback();
                continue;
            }

            try
            {
                connection.Execute(ReadEmbeddedSql(resourceName), transaction: transaction);
                connection.Execute(
                    "INSERT INTO meta(key, value) VALUES('schema_version', @version) "
                    + "ON CONFLICT(key) DO UPDATE SET value = excluded.value",
                    new { version = version.ToString(CultureInfo.InvariantCulture) },
                    transaction);
                transaction.Commit();
            }
            catch (SqliteException ex)
            {
                throw new SkillStatsStartupException($"applying database migration {version} failed", ex);
            }

            applied++;
        }

        return applied;
    }

    /// <summary>Read the stored schema version, treating a missing meta table as 0.</summary>
    /// <param name="connection">An open connection.</param>
    /// <returns>The current schema version.</returns>
    public static long CurrentVersion(SqliteConnection connection)
        => CurrentVersion(connection, null);

    private static long CurrentVersion(SqliteConnection connection, SqliteTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);
        try
        {
            var value = connection.ExecuteScalar<string>(
                "SELECT value FROM meta WHERE key = 'schema_version'",
                transaction: transaction);
            return value is null ? 0 : long.Parse(value, CultureInfo.InvariantCulture);
        }
        catch (SqliteException)
        {
            // The meta table does not exist yet: a brand-new database.
            return 0;
        }
    }

    private static string ReadEmbeddedSql(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new SkillStatsStartupException($"embedded migration resource '{resourceName}' is missing");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}