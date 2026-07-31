using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using RaccoonNinja.McpToolset.Files.Storage;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Journal;

/// <summary>
/// The append-only mutation journal: a SQLite database of batches and their file rows, plus a
/// content-addressed pre-image blob store. It implements the write-ahead ordering that makes a mid-batch
/// crash recoverable. Per file the caller: (1) puts the pre-image blob; (2) calls
/// <see cref="BeginBatch"/> to commit the batch and all its file rows as <see cref="JournalOutcome.Pending"/>
/// before any disk write; (3) performs the atomic writes; (4) calls <see cref="FinalizeChanged"/> to record
/// each post-image hash and flip the rows to <see cref="JournalOutcome.Changed"/>. A crash between steps 3
/// and 4 leaves rows <see cref="JournalOutcome.Pending"/>, which undo still restores from their pre-image.
/// </summary>
public sealed class JournalStore
{
    private const long SchemaVersion = 1;

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS meta (
          key   TEXT PRIMARY KEY,
          value TEXT NOT NULL
        ) WITHOUT ROWID;

        CREATE TABLE IF NOT EXISTS batches (
          batch_id     INTEGER PRIMARY KEY AUTOINCREMENT,
          created_utc  TEXT NOT NULL,
          tool         TEXT NOT NULL,
          root_name    TEXT NOT NULL,
          args_summary TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS files (
          id                       INTEGER PRIMARY KEY AUTOINCREMENT,
          batch_id                 INTEGER NOT NULL REFERENCES batches(batch_id) ON DELETE CASCADE,
          path                     TEXT NOT NULL,
          pre_hash                 TEXT NOT NULL,
          blob_ref                 TEXT NOT NULL,
          post_hash                TEXT,
          encoding                 TEXT NOT NULL,
          encoding_confidence      REAL NOT NULL,
          ladder_step              TEXT NOT NULL,
          source_encoding_supplied INTEGER NOT NULL,
          outcome                  TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_files_batch ON files(batch_id);
        """;

    private const int BusyTimeoutMs = 5_000;

    private static readonly object DapperInit = InitDapper();

    private readonly string _connectionString;
    private readonly BlobStore _blobs;

    /// <summary>Create the store over the resolved journal paths.</summary>
    /// <param name="paths">The resolved journal directory and its database and blob locations.</param>
    public JournalStore(JournalPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _ = DapperInit;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DbPath,
            ForeignKeys = true,
            Pooling = true,
        }.ToString();
        _blobs = new BlobStore(paths.BlobsDir);
    }

    /// <summary>Create the schema and record the schema version. Idempotent (forward-only).</summary>
    public void EnsureSchema()
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        connection.Execute(SchemaSql, transaction: transaction);
        connection.Execute(
            "INSERT INTO meta(key, value) VALUES('schema_version', @Version) "
            + "ON CONFLICT(key) DO UPDATE SET value = excluded.value",
            new { Version = SchemaVersion.ToString(CultureInfo.InvariantCulture) },
            transaction);
        transaction.Commit();
    }

    /// <summary>Store a pre-image and return its content-addressed reference (dedup and orphan-tolerant).</summary>
    /// <param name="content">The pre-image bytes.</param>
    /// <returns>The blob reference.</returns>
    public string PutPreImage(byte[] content) => _blobs.Put(content);

    /// <summary>Read a pre-image blob by reference.</summary>
    /// <param name="blobRef">The blob reference recorded on a file row.</param>
    /// <returns>The pre-image bytes.</returns>
    public byte[] ReadPreImage(string blobRef) => _blobs.Read(blobRef);

    /// <summary>
    /// Commit a batch and all its pending file rows in one transaction, before any disk write (E12 step 2).
    /// </summary>
    /// <param name="tool">The tool producing the batch.</param>
    /// <param name="rootName">The edited root's agent-facing name.</param>
    /// <param name="argsSummary">A machine-privacy-safe argument summary.</param>
    /// <param name="pendingFiles">The rows to record as <see cref="JournalOutcome.Pending"/> (pre-image set, post-image null).</param>
    /// <returns>The new monotonic batch id.</returns>
    public long BeginBatch(string tool, string rootName, string argsSummary, IReadOnlyList<FileRecord> pendingFiles)
    {
        ArgumentNullException.ThrowIfNull(pendingFiles);

        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);

        var batchId = connection.ExecuteScalar<long>(
            "INSERT INTO batches(created_utc, tool, root_name, args_summary) "
            + "VALUES(@Created, @Tool, @Root, @Args) RETURNING batch_id",
            new
            {
                Created = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                Tool = tool,
                Root = rootName,
                Args = argsSummary,
            },
            transaction);

        foreach (var file in pendingFiles)
        {
            connection.Execute(
                "INSERT INTO files(batch_id, path, pre_hash, blob_ref, post_hash, encoding, "
                + "encoding_confidence, ladder_step, source_encoding_supplied, outcome) "
                + "VALUES(@BatchId, @Path, @PreHash, @BlobRef, NULL, @Encoding, @Confidence, @Ladder, @Supplied, @Outcome)",
                new
                {
                    BatchId = batchId,
                    file.Path,
                    file.PreHash,
                    file.BlobRef,
                    file.Encoding,
                    Confidence = file.EncodingConfidence,
                    Ladder = file.LadderStep,
                    Supplied = file.SourceEncodingSupplied,
                    Outcome = JournalOutcome.Pending,
                },
                transaction);
        }

        transaction.Commit();
        return batchId;
    }

    /// <summary>Record each file's post-image hash and flip it to <see cref="JournalOutcome.Changed"/> (E12 step 4).</summary>
    /// <param name="batchId">The batch whose rows are being finalized.</param>
    /// <param name="postHashByPath">The post-image hash for each changed path.</param>
    public void FinalizeChanged(long batchId, IReadOnlyDictionary<string, string> postHashByPath)
    {
        ArgumentNullException.ThrowIfNull(postHashByPath);

        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        foreach (var (path, postHash) in postHashByPath)
        {
            connection.Execute(
                "UPDATE files SET post_hash = @Post, outcome = @Changed "
                + "WHERE batch_id = @BatchId AND path = @Path AND outcome = @Pending",
                new
                {
                    Post = postHash,
                    Changed = JournalOutcome.Changed,
                    BatchId = batchId,
                    Path = path,
                    Pending = JournalOutcome.Pending,
                },
                transaction);
        }

        transaction.Commit();
    }

    /// <summary>Return a compact summary of the most recent batches, newest first, each with its changed-file count.</summary>
    /// <param name="limit">The maximum number of batches to return.</param>
    /// <returns>The batch summaries, newest first.</returns>
    public IReadOnlyList<BatchSummary> ListRecent(int limit)
    {
        using var connection = Open();
        return connection.Query<BatchSummary>(
            "SELECT b.batch_id, b.created_utc, b.tool, COUNT(f.id) AS changed_count "
            + "FROM batches b LEFT JOIN files f ON f.batch_id = b.batch_id "
            + "GROUP BY b.batch_id, b.created_utc, b.tool "
            + "ORDER BY b.batch_id DESC LIMIT @Limit",
            new { Limit = limit }).AsList();
    }

    /// <summary>Return one batch, or <c>null</c> when no batch has that id.</summary>
    /// <param name="batchId">The batch id.</param>
    /// <returns>The batch, or <c>null</c>.</returns>
    public Batch GetBatch(long batchId)
    {
        using var connection = Open();
        return connection.QuerySingleOrDefault<Batch>(
            "SELECT batch_id, created_utc, tool, root_name, args_summary FROM batches WHERE batch_id = @Id",
            new { Id = batchId });
    }

    /// <summary>Return the id of the most recent batch, or <c>null</c> when the journal is empty.</summary>
    /// <returns>The latest batch id, or <c>null</c>.</returns>
    public long? LatestBatchId()
    {
        using var connection = Open();
        return connection.ExecuteScalar<long?>("SELECT MAX(batch_id) FROM batches");
    }

    /// <summary>Return every file row of a batch (for undo).</summary>
    /// <param name="batchId">The batch id.</param>
    /// <returns>The file rows.</returns>
    public IReadOnlyList<FileRecord> GetBatchFiles(long batchId)
    {
        using var connection = Open();
        return connection.Query<FileRecord>(
            "SELECT path, pre_hash, blob_ref, post_hash, encoding, encoding_confidence, "
            + "ladder_step, source_encoding_supplied, outcome FROM files WHERE batch_id = @Id ORDER BY id",
            new { Id = batchId }).AsList();
    }

    /// <summary>
    /// Prune batches beyond the newest <paramref name="retentionBatches"/> or older than
    /// <paramref name="retentionHours"/>, deleting only the pre-image blobs no surviving batch still
    /// references. The droppable set is computed before the rows are deleted.
    /// </summary>
    /// <param name="retentionBatches">The number of most-recent batches always kept.</param>
    /// <param name="retentionHours">The age past which a batch is eligible for pruning.</param>
    /// <returns>The number of batches pruned.</returns>
    public int PruneRetention(int retentionBatches, int retentionHours)
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-retentionHours).ToString("O", CultureInfo.InvariantCulture);

        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);

        var deletable = connection.Query<long>(
            "SELECT batch_id FROM batches "
            + "WHERE batch_id NOT IN (SELECT batch_id FROM batches ORDER BY batch_id DESC LIMIT @Keep) "
            + "OR created_utc < @Cutoff",
            new { Keep = retentionBatches, Cutoff = cutoff },
            transaction).AsList();

        if (deletable.Count == 0)
        {
            transaction.Commit();
            return 0;
        }

        var droppable = connection.Query<string>(
            "SELECT DISTINCT blob_ref FROM files WHERE batch_id IN @Ids",
            new { Ids = deletable },
            transaction).ToHashSet(StringComparer.Ordinal);

        var surviving = connection.Query<string>(
            "SELECT DISTINCT blob_ref FROM files WHERE batch_id NOT IN @Ids",
            new { Ids = deletable },
            transaction).ToHashSet(StringComparer.Ordinal);

        droppable.ExceptWith(surviving);

        connection.Execute("DELETE FROM batches WHERE batch_id IN @Ids", new { Ids = deletable }, transaction);
        transaction.Commit();

        // Remove orphaned blobs after the rows are gone. A crash here only leaves orphan blobs, which the
        // content-addressed store tolerates; it never drops a blob a surviving batch still references.
        foreach (var blobRef in droppable)
        {
            _blobs.Remove(blobRef);
        }

        return deletable.Count;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        connection.Execute(string.Create(
            CultureInfo.InvariantCulture,
            $"PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout={BusyTimeoutMs};"));
        return connection;
    }

    private static object InitDapper()
    {
        // The schema is snake_case and the record properties are PascalCase; let Dapper bridge them.
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return new object();
    }
}