using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using RaccoonNinja.McpToolset.Server.FileVault.Configuration;

namespace RaccoonNinja.McpToolset.Server.FileVault.Storage;

/// <summary>
/// Opens configured SQLite connections against the vault database. <c>Foreign Keys=True</c> rides
/// the connection string (the driver re-applies it per physical open, pool-safe); WAL journal
/// mode, NORMAL synchronous, and the busy timeout are asserted per open — re-asserting is
/// harmless and keeps behavior explicit across pooled reuse.
/// </summary>
public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;
    private readonly int _busyTimeoutMs;

    /// <summary>Create the factory for the database at <see cref="VaultConfig.DbPath"/>.</summary>
    /// <param name="config">The resolved server configuration.</param>
    public SqliteConnectionFactory(VaultConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = config.DbPath,
            ForeignKeys = true,
            Pooling = true,
        }.ToString();
        _busyTimeoutMs = config.BusyTimeoutMs;
    }

    /// <summary>Open a connection with the vault pragmas applied.</summary>
    /// <returns>An open connection; the caller disposes it.</returns>
    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        connection.Execute(string.Create(
            CultureInfo.InvariantCulture,
            $"PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout={_busyTimeoutMs};"));
        return connection;
    }
}