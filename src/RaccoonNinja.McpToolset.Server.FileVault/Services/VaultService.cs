using System.Globalization;
using System.Text;
using System.Text.Json;
using RaccoonNinja.McpToolset.Server.FileVault.Configuration;
using RaccoonNinja.McpToolset.Server.FileVault.Domain;
using RaccoonNinja.McpToolset.Server.FileVault.Errors;
using RaccoonNinja.McpToolset.Server.FileVault.Extensions;
using RaccoonNinja.McpToolset.Server.FileVault.Storage;

namespace RaccoonNinja.McpToolset.Server.FileVault.Services;

/// <summary>
/// The domain core: every vault operation on top of the storage layer with optimistic
/// concurrency. All mutations funnel through <see cref="CommitWrite"/>, the single OCC write
/// path. Ordering there is load-bearing: the immutable snapshot is written to disk first, then
/// the database row commits — a crash in between leaves a harmless orphan file, never a dangling
/// pointer.
/// </summary>
public sealed class VaultService(IVaultRepository repository, FileStore files, VaultConfig config)
{
    /// <summary>Conflict diffs are capped so a rejected save cannot return an unbounded payload.</summary>
    private const int MaxDiffLines = 200;

    /// <summary>
    /// The LCS diff table is O(baseLines * currentLines); the hint is skipped above this product
    /// and the conflict degrades to hint-less, a shape the contract already allows. (The Rust
    /// server's LCS table is unbounded.)
    /// </summary>
    private const long MaxDiffLineProduct = 4_000_000;

    /// <summary>Metadata length caps the Rust server did not enforce.</summary>
    private const int MaxSummaryBytes = 16 * 1024;

    private const int MaxTagCount = 64;
    private const int MaxTagLength = 128;

    /// <summary>Save content as a new version (first save when <paramref name="baseVersion"/> is null).</summary>
    /// <param name="project">The resolved project.</param>
    /// <param name="name">The validated file name.</param>
    /// <param name="content">The full new content (replaces; not a delta).</param>
    /// <param name="summary">The one-line summary (always replaces on save).</param>
    /// <param name="baseVersion">The version being overwritten; omit only for the first-ever save.</param>
    /// <param name="tags">Non-null replaces the tag set; null keeps existing tags.</param>
    /// <param name="format">The content format.</param>
    /// <param name="parent">The parent-link change (save produces leave/set only).</param>
    /// <returns>The committed version and hash.</returns>
    public Committed Save(
        ProjectName project,
        FileName name,
        string content,
        string summary,
        int? baseVersion,
        IReadOnlyList<string> tags,
        VaultFormat format,
        ParentUpdate parent)
    {
        ValidateMetadata(summary, tags);
        return CommitWrite(project, name, content ?? string.Empty, format, summary ?? string.Empty, tags, baseVersion,
            VaultOp.Save, parent);
    }

    /// <summary>Append content to the current version, producing the next version.</summary>
    /// <param name="project">The resolved project.</param>
    /// <param name="name">The validated file name.</param>
    /// <param name="content">The content concatenated onto the current version (no separator).</param>
    /// <param name="baseVersion">The version being appended to.</param>
    /// <returns>The committed version and hash.</returns>
    public Committed Append(ProjectName project, FileName name, string content, int baseVersion)
    {
        var (format, text) = LoadActiveCurrent(project, name, baseVersion);
        var combined = text + (content ?? string.Empty);
        return CommitWrite(project, name, combined, format, summary: null, tags: null, baseVersion, VaultOp.Append,
            ParentUpdate.Leave);
    }

    /// <summary>Replace the body of a markdown section identified by its heading text.</summary>
    /// <param name="project">The resolved project.</param>
    /// <param name="name">The validated file name.</param>
    /// <param name="heading">The target heading text.</param>
    /// <param name="content">The new section body.</param>
    /// <param name="baseVersion">The version being edited.</param>
    /// <returns>The committed version and hash.</returns>
    public Committed EditSection(ProjectName project, FileName name, string heading, string content, int baseVersion)
    {
        var (format, text) = LoadActiveCurrent(project, name, baseVersion);
        if (format != VaultFormat.Markdown)
        {
            throw VaultException.InvalidFormat("edit_section can only edit markdown files");
        }

        var updated = SectionEditor.SpliceSection(text, heading, content);
        return CommitWrite(project, name, updated, VaultFormat.Markdown, summary: null, tags: null, baseVersion,
            VaultOp.EditSection, ParentUpdate.Leave);
    }

    /// <summary>Set the value at a dotted key path in a JSON or YAML document.</summary>
    /// <param name="project">The resolved project.</param>
    /// <param name="name">The validated file name.</param>
    /// <param name="keyPath">The dotted key path.</param>
    /// <param name="value">The new value.</param>
    /// <param name="baseVersion">The version being edited.</param>
    /// <returns>The committed version and hash.</returns>
    public Committed EditKey(ProjectName project, FileName name, string keyPath, JsonElement value, int baseVersion)
    {
        var (format, text) = LoadActiveCurrent(project, name, baseVersion);
        var updated = format switch
        {
            VaultFormat.Json => KeyEditor.SetJsonKey(text, keyPath, value),
            VaultFormat.Yaml => KeyEditor.SetYamlKey(text, keyPath, value),
            _ => throw VaultException.InvalidFormat("edit_key can only edit json or yaml files"),
        };
        return CommitWrite(project, name, updated, format, summary: null, tags: null, baseVersion, VaultOp.EditKey,
            ParentUpdate.Leave);
    }

    /// <summary>Apply a metadata-only change and return the file's (unchanged) current version.</summary>
    /// <param name="project">The resolved project.</param>
    /// <param name="name">The validated file name.</param>
    /// <param name="summary">Non-null replaces the summary.</param>
    /// <param name="tags">Non-null replaces the tag set.</param>
    /// <param name="parent">Tri-state parent change.</param>
    /// <param name="baseVersion">Optional staleness guard; the version is never bumped.</param>
    /// <returns>The unchanged current version.</returns>
    public int SetMeta(
        ProjectName project,
        FileName name,
        string summary,
        IReadOnlyList<string> tags,
        ParentUpdate parent,
        int? baseVersion)
    {
        if (summary is null && tags is null && parent.IsLeave)
        {
            throw VaultException.NothingToUpdate();
        }

        ValidateMetadata(summary, tags);
        return repository.SetMeta(new MetaUpdate
        {
            Project = project.Value,
            Name = name.Value,
            Summary = summary,
            Tags = tags,
            Parent = parent,
            BaseVersion = baseVersion,
        });
    }

    /// <summary>Fetch a file's content and metadata, including parent and children.</summary>
    /// <param name="project">The resolved project.</param>
    /// <param name="name">The validated file name.</param>
    /// <param name="version">The version to fetch, or null for the current one.</param>
    /// <returns>The record, decoded content, and children.</returns>
    public (FileRecord Record, string Content, IReadOnlyList<ChildRow> Children) Get(
        ProjectName project,
        FileName name,
        int? version)
    {
        var record = version is { } v
            ? repository.GetVersion(project.Value, name.Value, v)
            : repository.GetCurrent(project.Value, name.Value);
        var content = ReadSnapshotText(record.RelPath);
        var children = repository.Children(project.Value, name.Value);
        return (record, content, children);
    }

    /// <summary>List active files, optionally filtered by project, required tags, and an FTS query.</summary>
    /// <param name="project">The project filter, or null for all projects.</param>
    /// <param name="tags">Required tags (all must match).</param>
    /// <param name="query">The FTS query, or null.</param>
    /// <returns>The matching rows.</returns>
    public IReadOnlyList<FileSummaryRow> List(ProjectName project, IReadOnlyList<string> tags, string query)
        => repository.List(new ListFilter { Project = project?.Value, Tags = tags ?? [], Query = query, });

    /// <summary>Return the version history of a file, newest first.</summary>
    /// <param name="project">The resolved project.</param>
    /// <param name="name">The validated file name.</param>
    /// <returns>The version rows.</returns>
    public IReadOnlyList<VersionRow> History(ProjectName project, FileName name)
        => repository.History(project.Value, name.Value);

    /// <summary>Archive a file (soft delete). Recoverable with <see cref="Restore"/>.</summary>
    /// <param name="project">The resolved project.</param>
    /// <param name="name">The validated file name.</param>
    public void Archive(ProjectName project, FileName name)
        => repository.SetState(project.Value, name.Value, FileState.Archived);

    /// <summary>Restore a previously archived file.</summary>
    /// <param name="project">The resolved project.</param>
    /// <param name="name">The validated file name.</param>
    public void Restore(ProjectName project, FileName name)
        => repository.SetState(project.Value, name.Value, FileState.Active);

    /// <summary>
    /// Permanently remove a file and every version. The database record is always deleted; the
    /// on-disk snapshots are deleted or retained (renamed with a <c>DELETED_</c> prefix) per
    /// <see cref="VaultConfig.PurgeDeleteFiles"/>.
    /// </summary>
    /// <param name="project">The resolved project.</param>
    /// <param name="name">The validated file name.</param>
    /// <param name="confirm">Must be <c>true</c>; checked before any deletion.</param>
    public void Purge(ProjectName project, FileName name, bool confirm)
    {
        if (!confirm)
        {
            throw VaultException.ConfirmationRequired();
        }

        var relPaths = repository.Purge(project.Value, name.Value);
        if (relPaths.Count == 0)
        {
            return;
        }

        // Second shared-snapshot check: between the purge commit above and the unlink below, another process
        // may have re-created the file with an identical (hash-derived) snapshot path. Removing
        // it would leave that process's committed row pointing at nothing.
        var resurrected = new HashSet<string>(
            repository.StillReferenced(relPaths), StringComparer.OrdinalIgnoreCase);
        var removable = relPaths.Where(path => !resurrected.Contains(path)).ToList();
        if (config.PurgeDeleteFiles)
        {
            files.RemoveSnapshots(removable);
        }
        else
        {
            files.RetainSnapshots(removable);
        }
    }

    /// <summary>The single optimistic-concurrency write path shared by save, append, and the editors.</summary>
    private Committed CommitWrite(
        ProjectName project,
        FileName name,
        string content,
        VaultFormat format,
        string summary,
        IReadOnlyList<string> tags,
        int? baseVersion,
        VaultOp op,
        ParentUpdate parent)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        if (bytes.LongLength > config.MaxContentBytes)
        {
            throw VaultException.TooLarge(config.MaxContentBytes, bytes.LongLength);
        }

        var hash = ContentHash.Of(bytes);
        var newVersion = (baseVersion ?? 0) + 1;
        var relPath = $"{project.Value}/{name.Value}/v{newVersion:D4}-{hash.ShortHex}.{format.Extension()}";

        files.WriteSnapshot(relPath, bytes);

        var now = VaultClock.NowUnixSeconds();
        try
        {
            // A Clear has nothing to clear on a brand-new file, so only Set carries a name.
            // If the named parent does not exist, CreateFirst rejects the write after the
            // snapshot was already written; that leaves only a harmless orphan snapshot,
            // never a dangling DB row — the same trade-off as the crash window.
            var committed = baseVersion is null
                ? repository.CreateFirst(new NewFile
                {
                    Project = project.Value,
                    Name = name.Value,
                    Format = format,
                    Summary = summary ?? string.Empty,
                    Tags = tags ?? [],
                    Hash = hash,
                    RelPath = relPath,
                    ByteSize = bytes.LongLength,
                    Parent = parent.ParentName,
                    CreatedAt = now,
                })
                : repository.CommitVersion(new VersionedWrite
                {
                    Project = project.Value,
                    Name = name.Value,
                    BaseVersion = baseVersion.Value,
                    NewVersion = newVersion,
                    Format = format,
                    Summary = summary,
                    Tags = tags,
                    Hash = hash,
                    RelPath = relPath,
                    ByteSize = bytes.LongLength,
                    Op = op,
                    Parent = parent,
                    UpdatedAt = now,
                });

            return committed with { ContentChars = content.Length, SplitHint = ComposeSplitHint(content.Length), };
        }
        catch (VaultException ex) when (ex.Code == VaultErrorCode.Conflict && ex.Diff is null)
        {
            throw EnrichConflict(ex, project, name, baseVersion);
        }
    }

    /// <summary>
    /// Compose the advisory split hint for a committed write, or null when the content is at
    /// or under <see cref="VaultConfig.SplitHintChars"/> (or the feature is disabled via 0). The
    /// sentence is built only from the server-measured length — never from content, summaries,
    /// tags, or names — and must stay that way.
    /// </summary>
    private string ComposeSplitHint(int contentChars)
        => config.SplitHintChars > 0 && contentChars > config.SplitHintChars
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"content is {contentChars} chars; consider keeping this note as a summary + index and moving detail into child notes linked via parent")
            : null;

    /// <summary>
    /// Attach a base-to-current line diff to a hint-less conflict so the caller can re-apply
    /// instead of blindly re-reading. Enrichment requires a supplied base version, both snapshots
    /// readable, and a bounded line-count product; any failure degrades gracefully back to
    /// the hint-less conflict.
    /// </summary>
    private VaultException EnrichConflict(
        VaultException conflict,
        ProjectName project,
        FileName name,
        int? baseVersion)
    {
        if (baseVersion is not { } baseV || conflict.CurrentVersion is not { } currentVersion)
        {
            return conflict;
        }

        string baseText;
        string currentText;
        try
        {
            baseText = ReadSnapshotText(repository.GetVersion(project.Value, name.Value, baseV).RelPath);
            currentText = ReadSnapshotText(repository.GetCurrent(project.Value, name.Value).RelPath);
        }
        catch (VaultException)
        {
            return conflict;
        }
        catch (IOException)
        {
            return conflict;
        }

        if (LineDiff.CountLines(baseText) * LineDiff.CountLines(currentText) > MaxDiffLineProduct)
        {
            return conflict;
        }

        var diff = LineDiff.Compute(baseText, currentText, MaxDiffLines);
        return VaultException.ConflictWithHint(currentVersion, baseV, diff);
    }

    /// <summary>
    /// Load the current version's text and guard that the file is active and that the caller's
    /// base version matches what was actually read. Shared by append/edit. Without the version
    /// gate here, a writer landing between this read and the commit could make a mismatched
    /// <paramref name="baseVersion"/> pass the repository's OCC check and silently drop the
    /// intervening version's content from the lineage.
    /// </summary>
    private (VaultFormat Format, string Text) LoadActiveCurrent(ProjectName project, FileName name, int baseVersion)
    {
        var record = repository.GetCurrent(project.Value, name.Value);
        if (record.State == FileState.Archived)
        {
            throw VaultException.Archived(project.Value, name.Value);
        }

        return record.CurrentVersion == baseVersion
            ? (record.Format, ReadSnapshotText(record.RelPath))
            : throw EnrichConflict(VaultException.Conflict(record.CurrentVersion), project, name, baseVersion);
    }

    private string ReadSnapshotText(string relPath)
        => Encoding.UTF8.GetString(files.ReadSnapshot(relPath));

    /// <summary>Cap summary size and tag count/length; tags are otherwise unvalidated free text.</summary>
    private static void ValidateMetadata(string summary, IReadOnlyList<string> tags)
    {
        if (summary is not null)
        {
            var summaryBytes = Encoding.UTF8.GetByteCount(summary);
            if (summaryBytes > MaxSummaryBytes)
            {
                throw VaultException.TooLarge(MaxSummaryBytes, summaryBytes);
            }
        }

        if (tags is null)
        {
            return;
        }

        if (tags.Count > MaxTagCount)
        {
            throw VaultException.TooLarge(
                $"{tags.Count} tags were supplied, which exceeds the {MaxTagCount}-tag limit");
        }

        foreach (var tag in tags)
        {
            if (tag is not null && tag.Length > MaxTagLength)
            {
                throw VaultException.TooLarge(
                    $"a tag is {tag.Length} characters long, which exceeds the {MaxTagLength}-character limit");
            }
        }
    }
}