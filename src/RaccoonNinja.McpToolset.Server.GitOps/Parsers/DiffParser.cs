using RaccoonNinja.McpToolset.Server.GitOps.Models;

namespace RaccoonNinja.McpToolset.Server.GitOps.Parsers;

/// <summary>
/// Facade over the <c>git diff</c> output formats: <c>--numstat -z</c> companion data, the
/// textual unified diff (delegated to <see cref="UnifiedDiffReader"/>), and assembly of both
/// into a <see cref="DiffResult"/>.
/// </summary>
public static class DiffParser
{
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

            var parts = chunk.Split('\t');
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
    /// rename entries leave that blank and store the orig/new paths in the following two NUL-separated
    /// tokens, consuming them by advancing <paramref name="i"/>.
    /// </summary>
    private static bool TryResolvePath(string[] parts, string[] tokens, ref int i, out string path)
    {
        if (parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2]))
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