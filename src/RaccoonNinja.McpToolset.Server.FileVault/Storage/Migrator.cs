using System.Globalization;
using System.Reflection;
using Dapper;
using Microsoft.Data.Sqlite;
using RaccoonNinja.McpToolset.Server.FileVault.Exceptions;

namespace RaccoonNinja.McpToolset.Server.FileVault.Storage;

/// <summary>
/// Forward-only schema migrations driven by <c>meta(schema_version)</c>. Migrations are embedded
/// at build time and applied in order inside transactions, so a fresh database reaches the latest
/// version and re-running is idempotent. This port introduces no migrations beyond the Rust
/// server's 0001/0002, keeping stores interchangeable between the two implementations.
/// </summary>
public static class Migrator
{
    private static readonly (long Version, string ResourceName)[] Migrations =
    [
        (1, "RaccoonNinja.McpToolset.Server.FileVault.Storage.Schema.0001_init.sql"),
        (2, "RaccoonNinja.McpToolset.Server.FileVault.Storage.Schema.0002_hierarchy.sql"),
    ];

    /// <summary>Apply every pending migration and return the number applied.</summary>
    /// <param name="factory">The connection factory for the target database.</param>
    /// <returns>The count of migrations that ran.</returns>
    /// <exception cref="VaultStartupException">Thrown when a migration statement fails.</exception>
    public static int Run(SqliteConnectionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        using var connection = factory.Open();
        var current = CurrentVersion(connection);
        var applied = 0;

        foreach (var (version, resourceName) in Migrations)
        {
            if (version <= current)
            {
                continue;
            }

            using var transaction = connection.BeginTransaction();
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
                throw new VaultStartupException($"applying database migration {version} failed", ex);
            }

            applied++;
        }

        return applied;
    }

    /// <summary>Read the stored schema version, treating a missing meta table as 0.</summary>
    /// <param name="connection">An open vault connection.</param>
    /// <returns>The current schema version.</returns>
    public static long CurrentVersion(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        try
        {
            var value = connection.ExecuteScalar<string>("SELECT value FROM meta WHERE key = 'schema_version'");
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
            ?? throw new VaultStartupException($"embedded migration resource '{resourceName}' is missing");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}