using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Models;

namespace RaccoonNinja.McpToolset.Server.GitOps.Parsers;

/// <summary>Parser for <c>git blame --porcelain</c> output.</summary>
public static class BlameParser
{
    /// <summary>
    /// The <c>git log</c> pretty format the raw-author pass prints: each commit's id and recorded author name, separated by
    /// Unit Separator (0x1F), each record ended by Record Separator (0x1E). <c>%an</c> never goes through the mailmap.
    /// </summary>
    public const string RawAuthorPrettyFormat = "%H%x1f%an%x1e";

    private const char UnitSeparator = '\x1f';
    private const char RecordSeparator = '\x1e';

    /// <summary>
    /// Each line entry begins with <c>SHA orig_line final_line [count]</c>; metadata
    /// headers follow, then a single tab-prefixed content line.
    /// </summary>
    public static IReadOnlyList<BlameLine> Parse(byte[] raw)
    {
        var text = TextDecoding.Decode(raw);
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<BlameLine>();

        var lines = text.Split('\n');
        var commitMeta = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var result = new List<BlameLine>();

        var i = 0;
        while (i < lines.Length)
        {
            if (!TryParseEntryHeader(lines[i], out var sha, out var finalLine))
            {
                i++;
                continue;
            }
            i++;

            // Porcelain emits full headers only the first time a commit appears; later
            // references reuse the accumulated metadata, so meta is keyed per-SHA.
            var meta = GetOrAddCommitMeta(commitMeta, sha);
            var content = ConsumeMetadataAndContent(lines, ref i, meta);
            result.Add(BuildBlameLine(sha, finalLine, content, meta));
        }
        return result;
    }

    /// <summary>
    /// Parse the raw-author pass's output into each requested commit's recorded author name. An author name can hold the
    /// separator bytes, so the records must name exactly the requested commits, each once; text after the last Record
    /// Separator is an unterminated record and is not read.
    /// </summary>
    /// <param name="raw">The pass's standard output.</param>
    /// <param name="requestedHashes">The commit ids written to the pass's standard input.</param>
    /// <returns>The recorded author name of each requested commit, keyed by commit id.</returns>
    /// <exception cref="GitCommandException">The records do not name exactly the requested commits, each once.</exception>
    public static IReadOnlyDictionary<string, string> ParseRawAuthors(byte[] raw, IReadOnlySet<string> requestedHashes)
    {
        var records = TextDecoding.Decode(raw).Split(RecordSeparator);
        var authors = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < records.Length - 1; i++)
        {
            var record = records[i].TrimStart('\n');
            var separator = record.IndexOf(UnitSeparator);
            if (separator < 0)
            {
                throw AuthorListMismatch();
            }

            var hash = record[..separator];
            if (!requestedHashes.Contains(hash) || !authors.TryAdd(hash, record[(separator + 1)..]))
            {
                throw AuthorListMismatch();
            }
        }

        return authors.Count == requestedHashes.Count ? authors : throw AuthorListMismatch();
    }

    /// <summary>
    /// Put each line's recorded author in <see cref="BlameLine.AuthorName"/>, and the name git blame printed, which went
    /// through the repository's mailmap, in <see cref="BlameLine.MailmapAuthorName"/> when the two differ. A line whose
    /// commit has no entry (one not committed yet) is returned as it is.
    /// </summary>
    /// <param name="lines">The lines <see cref="Parse"/> returned.</param>
    /// <param name="rawAuthors">The recorded author name of each committed line's commit, keyed by commit id.</param>
    /// <returns>The lines, in order.</returns>
    public static IReadOnlyList<BlameLine> WithRawAuthors(IReadOnlyList<BlameLine> lines, IReadOnlyDictionary<string, string> rawAuthors)
        => [.. lines.Select(line => rawAuthors.TryGetValue(line.CommitHash, out var rawAuthor)
            ? line with
            {
                AuthorName = rawAuthor,
                MailmapAuthorName = string.Equals(line.AuthorName, rawAuthor, StringComparison.Ordinal) ? null : line.AuthorName,
            }
            : line)];

    /// <summary>
    /// Parse an entry header line (<c>SHA orig_line final_line [count]</c>). Returns
    /// <see langword="false"/> for blank or malformed lines so the caller can skip them.
    /// </summary>
    private static bool TryParseEntryHeader(string line, out string sha, out int finalLine)
    {
        sha = null;
        finalLine = 0;
        if (string.IsNullOrWhiteSpace(line)) return false;

        var parts = line.Split(' ');
        if (parts.Length < 3 || parts[0].Length != 40) return false;
        if (!int.TryParse(parts[2], out finalLine)) return false;

        sha = parts[0];
        return true;
    }

    /// <summary>Get the metadata dictionary for <paramref name="sha"/>, creating it on first sight.</summary>
    private static Dictionary<string, string> GetOrAddCommitMeta(
        Dictionary<string, Dictionary<string, string>> commitMeta, string sha)
    {
        if (!commitMeta.TryGetValue(sha, out var meta))
        {
            meta = new Dictionary<string, string>(StringComparer.Ordinal);
            commitMeta[sha] = meta;
        }
        return meta;
    }

    /// <summary>
    /// Advance <paramref name="i"/> past this entry's metadata headers and its single
    /// tab-prefixed content line, recording any new keys into <paramref name="meta"/>.
    /// Returns the entry content (empty if the stream ends before a content line).
    /// </summary>
    private static string ConsumeMetadataAndContent(string[] lines, ref int i, Dictionary<string, string> meta)
    {
        while (i < lines.Length)
        {
            var next = lines[i];
            if (next.StartsWith('\t'))
            {
                i++;
                return next.Substring(1);
            }

            var space = next.IndexOf(' ');
            if (space > 0)
            {
                var key = next.Substring(0, space);
                var value = next.Substring(space + 1);
                if (!meta.ContainsKey(key)) meta[key] = value;
            }
            i++;
        }
        return string.Empty;
    }

    /// <summary>Assemble a <see cref="BlameLine"/> from a parsed header, content, and commit metadata.</summary>
    private static BlameLine BuildBlameLine(string sha, int finalLine, string content, Dictionary<string, string> meta)
    {
        var authorName = meta.TryGetValue("author", out var an) ? an : string.Empty;
        var ts = meta.TryGetValue("author-time", out var atRaw) && long.TryParse(atRaw, out var atSec) ? atSec : 0L;
        var authored = DateTimeOffset.FromUnixTimeSeconds(ts);
        return new BlameLine
        {
            LineNo = finalLine,
            Content = content,
            CommitHash = sha,
            AuthorName = authorName,
            AuthoredAt = authored,
            RelativeTime = RelativeTime.Describe(authored),
        };
    }

    /// <summary>Build the fixed error for a raw-author pass whose records do not match the requested commits.</summary>
    /// <returns>The exception to throw; its message names no commit and no author.</returns>
    private static GitCommandException AuthorListMismatch()
        => new("git log returned an author list that does not match the blamed commits");
}