using Dapper;
using Microsoft.Data.Sqlite;
using RaccoonNinja.McpToolset.Server.FileVault.Domain;
using RaccoonNinja.McpToolset.Server.FileVault.Errors;
using RaccoonNinja.McpToolset.Server.FileVault.Extensions;
using RaccoonNinja.McpToolset.Server.FileVault.Services;

namespace RaccoonNinja.McpToolset.Server.FileVault.Storage;

/// <summary>
/// The SQLite repository, on Dapper over Microsoft.Data.Sqlite. All SQL is parameterized (Dapper
/// expands IN-lists into bound parameters, never interpolated values). Every mutating method takes
/// the cross-process write lock up front (the default
/// <see cref="SqliteConnection.BeginTransaction()"/> issues <c>BEGIN IMMEDIATE</c>) and runs the
/// optimistic-concurrency check inside that same transaction, so the check-and-commit is race-free
/// even across processes. The FTS index is kept in lockstep with <c>files</c> inside the same
/// transactions.
/// </summary>
public sealed class SqliteVaultRepository(SqliteConnectionFactory factory) : IVaultRepository
{
    private const int SqliteConstraintPrimary = 19;
    private const int MaxCycleWalkDepth = 10_000;

    /// <inheritdoc />
    public Committed CreateFirst(NewFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        using var connection = factory.Open();
        using var transaction = connection.BeginTransaction();

        // A brand-new file cannot yet be anyone's parent, so a parent link at creation only needs
        // the target to exist in the same project; no self/cycle check is possible or required.
        long? parentId = file.Parent is null
            ? null
            : ResolveParentTarget(connection, transaction, file.Project, file.Parent);

        try
        {
            connection.Execute(
                "INSERT INTO files(project, name, current_version, format, summary, state, parent_id, created_at, updated_at) "
                + "VALUES(@project, @name, 1, @format, @summary, 'active', @parent_id, @created_at, @created_at)",
                new
                {
                    project = file.Project,
                    name = file.Name,
                    format = file.Format.ToWireString(),
                    summary = file.Summary ?? string.Empty,
                    parent_id = parentId,
                    created_at = file.CreatedAt,
                },
                transaction);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintPrimary)
        {
            // Someone created this (project, name) first; surface the current version so the
            // caller can switch to an OCC update.
            var current = CurrentVersionOf(connection, transaction, file.Project, file.Name) ?? 0;
            throw VaultException.Conflict(current);
        }

        var fileId = connection.ExecuteScalar<long>("SELECT last_insert_rowid()", transaction: transaction);
        InsertVersionRow(
            connection, transaction, fileId, 1, file.Hash, file.RelPath, file.ByteSize,
            file.Summary ?? string.Empty, VaultOp.Save, file.CreatedAt);
        InsertTags(connection, transaction, fileId, file.Tags);
        SyncFts(connection, transaction, fileId, file.Name, file.Summary ?? string.Empty, string.Join(' ', file.Tags ?? []));

        transaction.Commit();
        return new Committed { Version = 1, Hash = file.Hash };
    }

    /// <inheritdoc />
    public Committed CommitVersion(VersionedWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);
        using var connection = factory.Open();
        using var transaction = connection.BeginTransaction();

        var row = LookupForWrite(connection, transaction, write.Project, write.Name)
            ?? throw VaultException.NotFound(write.Project, write.Name);

        // The archived check runs before the OCC gate: a stale write to an archived file yields
        // `archived`, not `conflict` (Rust parity).
        if (FileStateExtensions.ParseFileState(row.State) == FileState.Archived)
        {
            throw VaultException.Archived(write.Project, write.Name);
        }

        if (row.CurrentVersion != write.BaseVersion)
        {
            throw VaultException.Conflict(row.CurrentVersion);
        }

        var effectiveSummary = write.Summary ?? row.Summary;

        InsertVersionRow(
            connection, transaction, row.Id, write.NewVersion, write.Hash, write.RelPath,
            write.ByteSize, effectiveSummary, write.Op, write.UpdatedAt);

        connection.Execute(
            "UPDATE files SET current_version = @version, format = @format, summary = @summary, updated_at = @updated_at "
            + "WHERE id = @id",
            new
            {
                version = write.NewVersion,
                format = write.Format.ToWireString(),
                summary = effectiveSummary,
                updated_at = write.UpdatedAt,
                id = row.Id,
            },
            transaction);

        ApplyParentUpdate(connection, transaction, row.Id, write.Project, write.Name, write.Parent);

        // Replace tags only when the caller supplied a new set; otherwise keep the existing ones.
        IReadOnlyList<string> effectiveTags;
        if (write.Tags is not null)
        {
            DeleteTags(connection, transaction, row.Id);
            InsertTags(connection, transaction, row.Id, write.Tags);
            effectiveTags = write.Tags;
        }
        else
        {
            effectiveTags = LoadTags(connection, transaction, row.Id);
        }

        SyncFts(connection, transaction, row.Id, write.Name, effectiveSummary, string.Join(' ', effectiveTags));

        transaction.Commit();
        return new Committed { Version = write.NewVersion, Hash = write.Hash };
    }

    /// <inheritdoc />
    public int SetMeta(MetaUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        using var connection = factory.Open();
        using var transaction = connection.BeginTransaction();

        var row = LookupForWrite(connection, transaction, update.Project, update.Name)
            ?? throw VaultException.NotFound(update.Project, update.Name);

        if (FileStateExtensions.ParseFileState(row.State) == FileState.Archived)
        {
            throw VaultException.Archived(update.Project, update.Name);
        }

        // Optional optimistic-concurrency guard. A metadata update never bumps current_version,
        // so omitting the guard is safe (last-writer-wins on cosmetic metadata).
        if (update.BaseVersion is int guard && row.CurrentVersion != guard)
        {
            throw VaultException.Conflict(row.CurrentVersion);
        }

        var now = VaultClock.NowUnixSeconds();
        var effectiveSummary = row.Summary;
        if (update.Summary is not null)
        {
            effectiveSummary = update.Summary;
            connection.Execute(
                "UPDATE files SET summary = @summary, updated_at = @updated_at WHERE id = @id",
                new { summary = update.Summary, updated_at = now, id = row.Id },
                transaction);
        }
        else
        {
            connection.Execute(
                "UPDATE files SET updated_at = @updated_at WHERE id = @id",
                new { updated_at = now, id = row.Id },
                transaction);
        }

        // Replace the tag set only when a new one was supplied.
        if (update.Tags is not null)
        {
            DeleteTags(connection, transaction, row.Id);
            InsertTags(connection, transaction, row.Id, update.Tags);
        }

        ApplyParentUpdate(connection, transaction, row.Id, update.Project, update.Name, update.Parent);

        // FTS indexes name/summary/tags only; a parent-only update leaves the index untouched.
        if (update.Summary is not null || update.Tags is not null)
        {
            var tagsJoined = update.Tags is not null
                ? string.Join(' ', update.Tags)
                : string.Join(' ', LoadTags(connection, transaction, row.Id));
            SyncFts(connection, transaction, row.Id, update.Name, effectiveSummary, tagsJoined);
        }

        transaction.Commit();
        return row.CurrentVersion;
    }

    /// <inheritdoc />
    public IReadOnlyList<ChildRow> Children(string project, string name)
    {
        using var connection = factory.Open();
        var parentId = RequireId(connection, null, project, name);

        return connection
            .Query<(string Name, string Summary)>(
                "SELECT name, summary FROM files WHERE parent_id = @parent_id AND state = 'active' ORDER BY name ASC",
                new { parent_id = parentId })
            .Select(row => new ChildRow { Name = row.Name, Summary = row.Summary })
            .ToList();
    }

    /// <inheritdoc />
    public FileRecord GetCurrent(string project, string name)
    {
        using var connection = factory.Open();
        return FetchRecord(connection, project, name, version: null);
    }

    /// <inheritdoc />
    public FileRecord GetVersion(string project, string name, int version)
    {
        using var connection = factory.Open();
        return FetchRecord(connection, project, name, version);
    }

    /// <inheritdoc />
    public IReadOnlyList<FileSummaryRow> List(ListFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        using var connection = factory.Open();

        // Optional FTS pre-filter producing a set of matching file ids.
        List<long> ftsIds = null;
        var ftsQuery = BuildFtsQuery(filter.Query);
        if (ftsQuery is not null)
        {
            ftsIds = connection
                .Query<long>("SELECT rowid FROM files_fts WHERE files_fts MATCH @query", new { query = ftsQuery })
                .ToList();
            if (ftsIds.Count == 0)
            {
                return [];
            }
        }

        var sql =
            "SELECT f.id, f.project, f.name, f.current_version, f.summary, f.updated_at, p.name "
            + "FROM files f LEFT JOIN files p ON f.parent_id = p.id WHERE f.state = 'active'";
        var parameters = new DynamicParameters();
        if (filter.Project is not null)
        {
            sql += " AND f.project = @project";
            parameters.Add("project", filter.Project);
        }

        sql += " ORDER BY f.updated_at DESC, f.name ASC";

        var raw = connection
            .Query<(long Id, string Project, string Name, int CurrentVersion, string Summary, long UpdatedAt, string Parent)>(
                sql, parameters)
            .ToList();

        // The FTS id filter is applied in memory: a SQL IN-list binds one parameter per id and
        // a broad FTS hit set on a large vault would blow SQLITE_MAX_VARIABLE_NUMBER.
        if (ftsIds is not null)
        {
            var idSet = ftsIds.ToHashSet();
            raw = raw.Where(row => idSet.Contains(row.Id)).ToList();
        }

        // Bulk-load tags for the matched files, then attach and apply the tag filter in memory.
        var tagMap = LoadTagsBulk(connection, raw.Select(r => r.Id).ToList());
        var required = filter.Tags ?? [];
        var output = new List<FileSummaryRow>(raw.Count);
        foreach (var row in raw)
        {
            var tags = tagMap.TryGetValue(row.Id, out var loaded) ? loaded : [];
            if (required.Count > 0 && !required.All(tags.Contains))
            {
                continue;
            }

            output.Add(new FileSummaryRow
            {
                Project = row.Project,
                Name = row.Name,
                CurrentVersion = row.CurrentVersion,
                Summary = row.Summary,
                UpdatedAt = row.UpdatedAt,
                Parent = row.Parent,
                Tags = tags,
            });
        }

        return output;
    }

    /// <inheritdoc />
    public IReadOnlyList<VersionRow> History(string project, string name)
    {
        using var connection = factory.Open();
        var fileId = RequireId(connection, null, project, name);

        return connection
            .Query<(int Version, string Op, string Summary, long ByteSize, string Hash, long CreatedAt)>(
                "SELECT version, op, summary, byte_size, content_hash, created_at "
                + "FROM versions WHERE file_id = @file_id ORDER BY version DESC",
                new { file_id = fileId })
            .Select(row => new VersionRow
            {
                Version = row.Version,
                Op = VaultOpExtensions.ParseVaultOp(row.Op),
                Summary = row.Summary,
                ByteSize = row.ByteSize,
                Hash = ContentHash.FromHex(row.Hash),
                CreatedAt = row.CreatedAt,
            })
            .ToList();
    }

    /// <inheritdoc />
    public void SetState(string project, string name, FileState state)
    {
        using var connection = factory.Open();
        var affected = connection.Execute(
            "UPDATE files SET state = @state, updated_at = @updated_at WHERE project = @project AND name = @name",
            new
            {
                state = state.ToDbString(),
                updated_at = VaultClock.NowUnixSeconds(),
                project,
                name,
            });
        if (affected == 0)
        {
            throw VaultException.NotFound(project, name);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Purge(string project, string name)
    {
        using var connection = factory.Open();
        using var transaction = connection.BeginTransaction();

        var fileId = RequireId(connection, transaction, project, name);

        // Exclude any snapshot path still referenced by another file's version rows, so a
        // case-insensitive filesystem collision cannot orphan a sibling note's only snapshot.
        var relPaths = connection
            .Query<string>(
                "SELECT v.rel_path FROM versions v WHERE v.file_id = @file_id "
                + "AND NOT EXISTS (SELECT 1 FROM versions o WHERE o.file_id <> @file_id AND o.rel_path = v.rel_path COLLATE NOCASE)",
                new { file_id = fileId },
                transaction)
            .ToList();

        // ON DELETE CASCADE removes versions and tags; the FTS index is not covered by the
        // foreign key, so it is deleted explicitly.
        connection.Execute("DELETE FROM files WHERE id = @id", new { id = fileId }, transaction);
        connection.Execute("DELETE FROM files_fts WHERE rowid = @id", new { id = fileId }, transaction);

        transaction.Commit();
        return relPaths;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> StillReferenced(IReadOnlyList<string> relPaths)
    {
        ArgumentNullException.ThrowIfNull(relPaths);
        if (relPaths.Count == 0)
        {
            return [];
        }

        using var connection = factory.Open();
        var referenced = new List<string>();
        // Chunked so a many-version purge can never exceed SQLite's bound-parameter limit.
        foreach (var chunk in relPaths.Chunk(500))
        {
            referenced.AddRange(connection.Query<string>(
                "SELECT DISTINCT rel_path FROM versions WHERE rel_path COLLATE NOCASE IN @paths",
                new { paths = chunk }));
        }

        return referenced;
    }

    /// <summary>
    /// Build a safe FTS5 MATCH expression from free user text: each whitespace-separated token
    /// becomes a quoted term (AND-combined), so user punctuation cannot create invalid query
    /// syntax. Returns <c>null</c> when the input has no usable tokens (no filter, not zero hits).
    /// </summary>
    internal static string BuildFtsQuery(string raw)
    {
        if (raw is null)
        {
            return null;
        }

        var terms = raw
            .Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => "\"" + token.Replace("\"", "\"\"") + "\"")
            .ToArray();
        return terms.Length == 0 ? null : string.Join(' ', terms);
    }

    private static FileRecord FetchRecord(SqliteConnection connection, string project, string name, int? version)
    {
        var header = connection
            .Query<(long Id, string Format, string State, int CurrentVersion, string Summary, string Parent)>(
                "SELECT f.id, f.format, f.state, f.current_version, f.summary, p.name "
                + "FROM files f LEFT JOIN files p ON f.parent_id = p.id "
                + "WHERE f.project = @project AND f.name = @name",
                new { project, name })
            .ToList();
        if (header.Count == 0)
        {
            throw VaultException.NotFound(project, name);
        }

        var file = header[0];
        var target = version ?? file.CurrentVersion;

        var versions = connection
            .Query<(string ContentHash, string RelPath, long ByteSize, string Summary)>(
                "SELECT content_hash, rel_path, byte_size, summary FROM versions "
                + "WHERE file_id = @file_id AND version = @version",
                new { file_id = file.Id, version = target })
            .ToList();
        if (versions.Count == 0)
        {
            throw VaultException.NotFound(project, $"{name} (version {target})");
        }

        var row = versions[0];

        // files.summary is the live, set_meta-mutable metadata; versions.summary is the capture
        // taken when that version was written. The default read returns the live summary; an
        // explicit version request returns the captured one, matching vault_history.
        var summary = version is null ? file.Summary : row.Summary;

        return new FileRecord
        {
            Project = project,
            Name = name,
            Format = VaultFormatExtensions.ParseVaultFormat(file.Format),
            Summary = summary,
            Tags = LoadTags(connection, null, file.Id),
            Version = target,
            Hash = ContentHash.FromHex(row.ContentHash),
            RelPath = row.RelPath,
            ByteSize = row.ByteSize,
            State = FileStateExtensions.ParseFileState(file.State),
            Parent = file.Parent,
            CurrentVersion = file.CurrentVersion,
        };
    }

    private static WriteRow LookupForWrite(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string project,
        string name)
        => connection.QuerySingleOrDefault<WriteRow>(
            "SELECT id AS Id, current_version AS CurrentVersion, state AS State, summary AS Summary "
            + "FROM files WHERE project = @project AND name = @name",
            new { project, name },
            transaction);

    private static long? CurrentIdOf(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string project,
        string name)
        => connection.ExecuteScalar<long?>(
            "SELECT id FROM files WHERE project = @project AND name = @name",
            new { project, name },
            transaction);

    private static long RequireId(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string project,
        string name)
        => CurrentIdOf(connection, transaction, project, name)
           ?? throw VaultException.NotFound(project, name);

    private static int? CurrentVersionOf(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string project,
        string name)
        => connection.ExecuteScalar<int?>(
            "SELECT current_version FROM files WHERE project = @project AND name = @name",
            new { project, name },
            transaction);

    /// <summary>
    /// Resolve a parent file's id within <paramref name="project"/>. Looking up by
    /// (project, name) is what keeps a parent same-project by construction.
    /// </summary>
    private static long ResolveParentTarget(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string project,
        string parent)
        => CurrentIdOf(connection, transaction, project, parent)
           ?? throw VaultException.ParentNotFound(project, parent);

    /// <summary>
    /// Apply a <see cref="ParentUpdate"/> to <paramref name="fileId"/>. Leave is a no-op; clear
    /// detaches to top-level; set resolves the named parent (same project), rejects self-links
    /// and cycles, then links.
    /// </summary>
    private static void ApplyParentUpdate(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long fileId,
        string project,
        string name,
        ParentUpdate update)
    {
        update ??= ParentUpdate.Leave;
        if (update.IsLeave)
        {
            return;
        }

        if (update.IsClear)
        {
            connection.Execute(
                "UPDATE files SET parent_id = NULL WHERE id = @id", new { id = fileId }, transaction);
            return;
        }

        var parentId = ResolveParentTarget(connection, transaction, project, update.ParentName);
        if (parentId == fileId)
        {
            throw VaultException.InvalidParent($"'{name}' cannot be its own parent");
        }

        AssertNoCycle(connection, transaction, fileId, parentId);

        connection.Execute(
            "UPDATE files SET parent_id = @parent_id WHERE id = @id",
            new { parent_id = parentId, id = fileId },
            transaction);
    }

    /// <summary>
    /// Reject a parent link that would create a cycle: walk upward from <paramref name="parentId"/>
    /// following <c>parent_id</c>; if <paramref name="fileId"/> is reached, linking would close a
    /// loop. A hard iteration cap guards against a pre-existing inconsistency producing an
    /// unbounded walk.
    /// </summary>
    private static void AssertNoCycle(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long fileId,
        long parentId)
    {
        long? cursor = parentId;
        var steps = 0;
        while (cursor is long current)
        {
            if (current == fileId)
            {
                throw VaultException.InvalidParent("the link would create a cycle in the hierarchy");
            }

            steps++;
            if (steps > MaxCycleWalkDepth)
            {
                throw VaultException.InvalidParent("hierarchy is too deep or already contains a cycle");
            }

            cursor = connection.ExecuteScalar<long?>(
                "SELECT parent_id FROM files WHERE id = @id", new { id = current }, transaction);
        }
    }

    private static void InsertVersionRow(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long fileId,
        int version,
        ContentHash hash,
        string relPath,
        long byteSize,
        string summary,
        VaultOp op,
        long createdAt)
        => connection.Execute(
            "INSERT INTO versions(file_id, version, content_hash, rel_path, byte_size, summary, op, created_at) "
            + "VALUES(@file_id, @version, @content_hash, @rel_path, @byte_size, @summary, @op, @created_at)",
            new
            {
                file_id = fileId,
                version,
                content_hash = hash.Hex,
                rel_path = relPath,
                byte_size = byteSize,
                summary,
                op = op.ToDbString(),
                created_at = createdAt,
            },
            transaction);

    private static void DeleteTags(SqliteConnection connection, SqliteTransaction transaction, long fileId)
        => connection.Execute("DELETE FROM tags WHERE file_id = @file_id", new { file_id = fileId }, transaction);

    private static void InsertTags(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long fileId,
        IReadOnlyList<string> tags)
    {
        if (tags is null || tags.Count == 0)
        {
            return;
        }

        // Dapper runs the statement once per element when given a sequence of parameter objects.
        connection.Execute(
            "INSERT OR IGNORE INTO tags(file_id, tag) VALUES(@file_id, @tag)",
            tags.Select(tag => new { file_id = fileId, tag }),
            transaction);
    }

    private static List<string> LoadTags(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long fileId)
        => connection
            .Query<string>(
                "SELECT tag FROM tags WHERE file_id = @file_id ORDER BY tag",
                new { file_id = fileId },
                transaction)
            .ToList();

    private static Dictionary<long, IReadOnlyList<string>> LoadTagsBulk(
        SqliteConnection connection,
        List<long> ids)
    {
        var map = new Dictionary<long, IReadOnlyList<string>>();
        if (ids.Count == 0)
        {
            return map;
        }

        // Dapper expands @ids into one bound integer parameter per element; chunked so a large
        // listing can never exceed SQLite's bound-parameter limit. An id never spans chunks, so
        // each per-id tag list stays fully sorted by the per-chunk ORDER BY.
        var working = new Dictionary<long, List<string>>();
        foreach (var chunk in ids.Chunk(500))
        {
            var rows = connection.Query<(long FileId, string Tag)>(
                "SELECT file_id, tag FROM tags WHERE file_id IN @ids ORDER BY tag",
                new { ids = chunk });
            foreach (var (id, tag) in rows)
            {
                if (!working.TryGetValue(id, out var list))
                {
                    list = [];
                    working[id] = list;
                }

                list.Add(tag);
            }
        }

        foreach (var (id, list) in working)
        {
            map[id] = list;
        }

        return map;
    }

    /// <summary>Replace the FTS row for <paramref name="fileId"/> (delete-then-insert keeps a standard FTS5 table consistent).</summary>
    private static void SyncFts(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long fileId,
        string name,
        string summary,
        string tagsJoined)
    {
        connection.Execute("DELETE FROM files_fts WHERE rowid = @id", new { id = fileId }, transaction);
        connection.Execute(
            "INSERT INTO files_fts(rowid, name, summary, tags) VALUES(@id, @name, @summary, @tags)",
            new { id = fileId, name, summary, tags = tagsJoined },
            transaction);
    }

    /// <summary>Header row for a write-path lookup; Dapper maps the aliased columns by name.</summary>
    private sealed class WriteRow
    {
        public long Id { get; init; }

        public int CurrentVersion { get; init; }

        public string State { get; init; }

        public string Summary { get; init; }
    }
}