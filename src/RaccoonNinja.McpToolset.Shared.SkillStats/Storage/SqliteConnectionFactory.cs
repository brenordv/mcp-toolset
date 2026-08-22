using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using RaccoonNinja.McpToolset.Shared.SkillStats.Configuration;

namespace RaccoonNinja.McpToolset.Shared.SkillStats.Storage;

/// <summary>
/// Opens SQLite connections against the skill-usage database in one of two modes. The ingest CLI
/// opens a writer (WAL journal, NORMAL synchronous, busy timeout); the read-only server opens a
/// reader (<c>Mode=ReadOnly</c>, <c>query_only</c>, busy timeout). Pragmas are asserted per open;
/// re-asserting is harmless and keeps behavior explicit across pooled reuse.
/// </summary>
public sealed class SqliteConnectionFactory
{
    private readonly string _writerConnectionString;
    private readonly string _readerConnectionString;

    /// <summary>Create the factory for the database at <see cref="SkillStatsConfig.DbPath"/>.</summary>
    /// <param name="config">The resolved configuration.</param>
    public SqliteConnectionFactory(SkillStatsConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _writerConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = config.DbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        }.ToString();
        _readerConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = config.DbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = true,
        }.ToString();
    }

    /// <summary>Open a read-write connection (creating the database if absent) with the writer pragmas applied.</summary>
    /// <returns>An open connection; the caller disposes it.</returns>
    public SqliteConnection OpenWriter()
    {
        var connection = new SqliteConnection(_writerConnectionString);
        connection.Open();
        connection.Execute(string.Create(
            CultureInfo.InvariantCulture,
            $"PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout={SkillStatsConfig.BusyTimeoutMs};"));
        return connection;
    }

    /// <summary>Open a read-only connection with the reader pragmas applied.</summary>
    /// <returns>An open connection; the caller disposes it.</returns>
    /// <remarks>
    /// Read-only access to the WAL database works because the store directory is user-owned and
    /// writable, so the <c>-shm</c>/<c>-wal</c> files are present or creatable.
    /// </remarks>
    public SqliteConnection OpenReader()
    {
        var connection = new SqliteConnection(_readerConnectionString);
        connection.Open();
        connection.Execute(string.Create(
            CultureInfo.InvariantCulture,
            $"PRAGMA query_only=1; PRAGMA busy_timeout={SkillStatsConfig.BusyTimeoutMs};"));
        return connection;
    }
}