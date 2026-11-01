using RaccoonNinja.McpToolset.Server.GitOps.Models;

namespace RaccoonNinja.McpToolset.Server.GitOps.Parsers;

/// <summary>
/// Facade over the <c>git diff</c> output formats: <c>--numstat -z</c> companion data, the
/// textual unified diff (delegated to <see cref="UnifiedDiffReader"/>), and assembly of both
/// into a <see cref="DiffResult"/>.
/// </summary>
public static class DiffParser
{
    /// <summary>
    /// Flags the <c>git diff</c> and <c>git show</c> patch passes add so the output has the one shape
    /// <see cref="ParseUnified"/> reads and holds every changed submodule, whatever the user's or the repository's git config
    /// says: no color escapes (<c>color.ui</c>, <c>color.diff</c>), the <c>a/</c> and <c>b/</c> header prefixes the reader
    /// splits on (<c>diff.noprefix</c>, <c>diff.mnemonicPrefix</c>, <c>diff.srcPrefix</c>, <c>diff.dstPrefix</c>), and the
    /// <c>short</c> submodule format, the only one that prints a <c>diff --git</c> header for a submodule and starts no git
    /// process inside it (<c>diff.submodule</c>). <c>--ignore-submodules=dirty</c> overrides
    /// <c>submodule.&lt;name&gt;.ignore</c>, which a committed <c>.gitmodules</c> can carry, and
    /// <c>diff.ignoreSubmodules</c>, either of which would drop a submodule whose recorded commit changed; unlike
    /// <c>none</c>, it also keeps a diff against the working tree from running <c>git status</c> inside each checked-out
    /// submodule, with that submodule's own config and filter drivers. <c>git stash show</c> gets the same pins from
    /// <see cref="StashPatchShapeFlags"/> and <see cref="StashHeaderPrefixConfig"/>.
    /// </summary>
    public static readonly IReadOnlyList<string> PatchShapeFlags =
    [
        "--no-color",
        "--src-prefix=a/",
        "--dst-prefix=b/",
        "--submodule=short",
        "--ignore-submodules=dirty",
    ];

    /// <summary>
    /// The <see cref="PatchShapeFlags"/> entries <c>git stash show</c> takes safely: no color escapes, the <c>short</c>
    /// submodule format, and <c>--ignore-submodules=dirty</c>. Its header prefixes come from
    /// <see cref="StashHeaderPrefixConfig"/>.
    /// </summary>
    public static readonly IReadOnlyList<string> StashPatchShapeFlags =
    [
        "--no-color",
        "--submodule=short",
        "--ignore-submodules=dirty",
    ];

    /// <summary>
    /// <c>-c</c> pairs that give <c>git stash show</c> the <c>a/</c> and <c>b/</c> header prefixes whatever
    /// <c>diff.noprefix</c>, <c>diff.mnemonicPrefix</c>, <c>diff.srcPrefix</c>, or <c>diff.dstPrefix</c> say. They are
    /// config rather than <c>--src-prefix=</c> and <c>--dst-prefix=</c> because <c>git stash show</c> in git 2.52.0
    /// through 2.55.x frees the arguments it parsed before it prints the diff, so a prefix it was given on the command
    /// line comes out garbled, while git copies config values into its own storage. A git older than 2.45 does not read
    /// <c>diff.srcPrefix</c> or <c>diff.dstPrefix</c>, from the command line or from a repository's config.
    /// </summary>
    public static readonly IReadOnlyList<(string Key, string Value)> StashHeaderPrefixConfig =
    [
        ("diff.noprefix", "false"),
        ("diff.mnemonicPrefix", "false"),
        ("diff.srcPrefix", "a/"),
        ("diff.dstPrefix", "b/"),
    ];

    private const string UnmergedPathPrefix = "* Unmerged path ";

    /// <summary>Parse <c>git diff --numstat -z</c> into <c>path → (additions, deletions)</c>.</summary>
    public static IReadOnlyDictionary<string, (int Additions, int Deletions)> ParseNumstatZ(byte[] raw)
    {
        var text = TextDecoding.Decode(raw);
        var tokens = text.Split('\0');
        var result = new Dictionary<string, (int, int)>(StringComparer.Ordinal);

        var i = 0;
        while (i < tokens.Length)
        {
            var chunk = tokens[i++];
            if (string.IsNullOrWhiteSpace(chunk)) continue;

            // Under -z the path is printed as it is and can hold a tab itself, so only the first two tabs separate fields.
            var parts = chunk.Split('\t', 3);
            if (parts.Length < 2) continue;
            if (!TryParseCount(parts[0], out var additions) || !TryParseCount(parts[1], out var deletions)) continue;

            if (TryResolvePath(parts, tokens, ref i, out var path))
            {
                result[path] = (additions, deletions);
            }
        }
        return result;
    }

    /// <summary>Parse a numstat count token; <c>"-"</c> (binary file) maps to zero. False when non-numeric.</summary>
    private static bool TryParseCount(string token, out int count)
    {
        if (token == "-")
        {
            count = 0;
            return true;
        }
        return int.TryParse(token, out count);
    }

    /// <summary>
    /// Resolve the path for a numstat record. Plain entries carry it in <paramref name="parts"/>[2];
    /// rename and copy entries leave that empty and store the orig/new paths in the following two NUL-separated
    /// tokens, consuming them by advancing <paramref name="i"/>.
    /// </summary>
    private static bool TryResolvePath(string[] parts, string[] tokens, ref int i, out string path)
    {
        // A path can be all whitespace; only a rename or copy record leaves this field empty.
        if (parts.Length >= 3 && parts[2].Length > 0)
        {
            path = parts[2];
            return true;
        }

        // Rename entry: next two tokens are orig + new paths; we key on the new path.
        if (i + 1 >= tokens.Length)
        {
            path = null;
            return false;
        }
        path = tokens[i + 1];
        i += 2;
        return true;
    }

    /// <summary>
    /// True when a file read from the patch has a path the <c>--numstat -z</c> pass does not list. That pass lists every
    /// path the patch prints, keyed on the new path for a rename or copy, so a miss is a path the reader misread from its
    /// header, or a working tree, index, or stash that changed between the two git processes. The check runs one way on
    /// purpose: the numstat pass also lists pairs the patch prints no <c>diff --git</c> header for (an unmerged path, a
    /// byte-identical pair).
    /// </summary>
    /// <param name="files">Files read from the patch with <see cref="ParseUnified"/>.</param>
    /// <param name="numstat">The map <see cref="ParseNumstatZ"/> returned for the same diff.</param>
    /// <returns><c>true</c> when at least one file's path is not a key of <paramref name="numstat"/>.</returns>
    public static bool HasPathMissingFromNumstat(
        IEnumerable<FileDiff> files,
        IReadOnlyDictionary<string, (int Additions, int Deletions)> numstat)
        => files.Any(file => !numstat.ContainsKey(file.Path));

    /// <summary>
    /// True when <paramref name="patch"/> has a <c>* Unmerged path</c> line and a path in <paramref name="numstat"/>
    /// holds a line break, or the numstat pass was cut at the output cap, so the list cannot show which names hold one.
    /// git writes an unmerged path's name raw on that line, unlike a <c>diff --git</c> header, so such a name can carry
    /// text that reads as a whole patch section.
    /// </summary>
    /// <param name="patch">Raw stdout bytes of the patch pass.</param>
    /// <param name="numstat">The map <see cref="ParseNumstatZ"/> returned for the same diff.</param>
    /// <param name="numstatTruncated">Whether the numstat pass hit the output cap.</param>
    /// <returns>
    /// <c>true</c> when <paramref name="patch"/> has at least one <c>* Unmerged path</c> line and either a key of
    /// <paramref name="numstat"/> holds a line break or <paramref name="numstatTruncated"/> is <c>true</c>.
    /// </returns>
    public static bool HasUnreadableUnmergedPath(
        byte[] patch,
        IReadOnlyDictionary<string, (int Additions, int Deletions)> numstat,
        bool numstatTruncated)
        => (numstatTruncated || numstat.Keys.Any(path => path.Contains('\n')))
           && TextDecoding.Decode(patch)
               .Split('\n')
               .Any(line => line.StartsWith(UnmergedPathPrefix, StringComparison.Ordinal));

    /// <summary>
    /// True when no file was read from the patch although the <c>--numstat -z</c> pass counted an added or deleted
    /// line. git writes a <c>diff --git</c> section for every pair with a changed line, so that combination means the
    /// patch was in a shape the reader does not recognize. A pair the numstat pass lists with 0 and 0 can have no
    /// section of its own: an unmerged path, or a binary file whose content matches the index while its timestamps do
    /// not, which counts as 0 and 0 since <see cref="ParseNumstatZ"/> reads its <c>-</c> counts as zero. git leaves a
    /// text file in that state out of the numstat pass.
    /// </summary>
    /// <param name="files">Files read from the patch with <see cref="ParseUnified"/>.</param>
    /// <param name="numstat">The map <see cref="ParseNumstatZ"/> returned for the same diff.</param>
    /// <returns>
    /// <c>true</c> when <paramref name="files"/> is empty and at least one entry of <paramref name="numstat"/> has a
    /// nonzero count.
    /// </returns>
    public static bool HasCountedLinesMissingFromPatch(
        IReadOnlyCollection<FileDiff> files,
        IReadOnlyDictionary<string, (int Additions, int Deletions)> numstat)
        => files.Count == 0 && numstat.Values.Any(counts => counts.Additions > 0 || counts.Deletions > 0);

    /// <summary>Parse <c>git diff --name-status -z</c> into <c>path → <see cref="ChangeType"/></c>.</summary>
    /// <param name="raw">Raw stdout bytes from <c>git diff --name-status -z</c>.</param>
    /// <returns>A map of (new) path to its change kind; empty when there is nothing to parse.</returns>
    public static IReadOnlyDictionary<string, ChangeType> ParseNameStatusZ(byte[] raw)
    {
        var text = TextDecoding.Decode(raw);
        var tokens = text.Split('\0');
        var result = new Dictionary<string, ChangeType>(StringComparer.Ordinal);

        var i = 0;
        while (i < tokens.Length)
        {
            var status = tokens[i++];
            if (string.IsNullOrWhiteSpace(status)) continue;

            if (TryResolveStatusPath(status, tokens, ref i, out var path) && !string.IsNullOrWhiteSpace(path))
            {
                result[path] = MapStatus(status[0]);
            }
        }
        return result;
    }

    /// <summary>
    /// Resolve the keyed path for a name-status record. Plain entries (A/M/D/T) carry the path in the
    /// single following NUL token; rename/copy entries (R/C) carry orig + new paths in the next two
    /// tokens, and we key on the new path, consuming the extra token by advancing <paramref name="i"/>.
    /// </summary>
    private static bool TryResolveStatusPath(string status, string[] tokens, ref int i, out string path)
    {
        if (i >= tokens.Length)
        {
            path = null;
            return false;
        }

        // Rename/copy: the next two tokens are orig + new paths; we key on the new path.
        if (status[0] is 'R' or 'C')
        {
            if (i + 1 >= tokens.Length)
            {
                path = null;
                return false;
            }
            path = tokens[i + 1];
            i += 2;
            return true;
        }

        path = tokens[i];
        i += 1;
        return true;
    }

    /// <summary>Map a <c>git --name-status</c> status letter to a <see cref="ChangeType"/>.</summary>
    /// <param name="status">The leading status character; any rename/copy similarity score is ignored.</param>
    /// <returns>The matching <see cref="ChangeType"/>, or <see cref="ChangeType.Unknown"/> when unrecognized.</returns>
    private static ChangeType MapStatus(char status) => status switch
    {
        'A' => ChangeType.Added,
        'M' => ChangeType.Modified,
        'D' => ChangeType.Deleted,
        'R' => ChangeType.Renamed,
        'C' => ChangeType.Copied,
        'T' => ChangeType.Modified,
        _ => ChangeType.Unknown,
    };

    /// <summary>Parse a textual unified diff (output of <c>git diff</c>) into per-file records.</summary>
    public static IReadOnlyList<FileDiff> ParseUnified(byte[] raw)
    {
        var text = TextDecoding.Decode(raw);
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<FileDiff>();

        return new UnifiedDiffReader().Read(text);
    }

    /// <summary>Assemble a <see cref="DiffResult"/> with optional numstat and change-type enrichment.</summary>
    public static DiffResult AssembleDiffResult(
        IReadOnlyList<FileDiff> files,
        string mode,
        bool truncated = false,
        IReadOnlyDictionary<string, (int Additions, int Deletions)> numstat = null,
        IReadOnlyDictionary<string, ChangeType> changeTypes = null)
    {
        var enriched = BuildEnrichedFiles(files, numstat, changeTypes);

        return new DiffResult
        {
            Files = enriched,
            TotalAdditions = enriched.Sum(file => file.Additions),
            TotalDeletions = enriched.Sum(file => file.Deletions),
            Mode = mode,
            Truncated = truncated,
        };
    }

    /// <summary>Overlay numstat counts and change kinds onto <paramref name="files"/>, leaving entries without a match untouched.</summary>
    private static List<FileDiff> BuildEnrichedFiles(
        IReadOnlyList<FileDiff> files,
        IReadOnlyDictionary<string, (int Additions, int Deletions)> numstat,
        IReadOnlyDictionary<string, ChangeType> changeTypes)
    {
        if (numstat is null && changeTypes is null)
        {
            return files.ToList();
        }
        return files.Select(file => EnrichFromNumstat(file, numstat))
                    .Select(file => EnrichChangeType(file, changeTypes))
                    .ToList();
    }

    /// <summary>Return <paramref name="file"/> with its counts replaced by the numstat entry when one exists.</summary>
    private static FileDiff EnrichFromNumstat(
        FileDiff file,
        IReadOnlyDictionary<string, (int Additions, int Deletions)> numstat)
    {
        if (numstat is null)
        {
            return file;
        }
        return numstat.TryGetValue(file.Path, out var counts)
            ? file with { Additions = counts.Additions, Deletions = counts.Deletions }
            : file;
    }

    /// <summary>
    /// Overlay a derived <see cref="ChangeType"/> onto <paramref name="file"/> when one is known for its
    /// path. Non-destructive: an already-resolved change kind is left untouched, so callers that derive
    /// the type elsewhere (e.g. the unified-diff path) are never clobbered.
    /// </summary>
    private static FileDiff EnrichChangeType(
        FileDiff file,
        IReadOnlyDictionary<string, ChangeType> changeTypes)
    {
        if (changeTypes is null || file.ChangeType is not ChangeType.Unknown)
        {
            return file;
        }
        return changeTypes.TryGetValue(file.Path, out var changeType)
            ? file with { ChangeType = changeType }
            : file;
    }
}