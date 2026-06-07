using RaccoonNinja.McpToolset.Server.GitOps.Models;

namespace RaccoonNinja.McpToolset.Server.GitOps.Parsers;

/// <summary>
/// Parser for <c>git log --pretty=format:%H%x1f%h%x1f...%x1e</c> output.
/// Fields are separated by Unit Separator (US, 0x1F); commits by Record Separator (RS, 0x1E).
/// </summary>
public static class LogParser
{
    /// <summary>Field order MUST match the pretty-format below.</summary>
    private static readonly string[] LogFieldOrder =
    [
        "hash",          // %H
        "short_hash",    // %h
        "parents",       // %P (space-separated SHAs)
        "author_name",   // %an
        "author_email",  // %ae
        "authored_at",   // %at (epoch seconds)
        "committed_at",  // %ct (epoch seconds)
        "subject",       // %s
        "body" // %b
    ];

    /// <summary>The git pretty-format spec corresponding to <see cref="LogFieldOrder"/>.</summary>
    public const string LogPrettyFormat = "%H%x1f%h%x1f%P%x1f%an%x1f%ae%x1f%at%x1f%ct%x1f%s%x1f%b%x1e";

    private const char UnitSeparator = '\x1f';
    private const char RecordSeparator = '\x1e';

    /// <summary>Parse the output of <c>git log --pretty=format:%H%x1f...%x1e</c>.</summary>
    public static IReadOnlyList<Commit> Parse(byte[] raw)
    {
        var text = TextDecoding.Decode(raw);
        if (string.IsNullOrEmpty(text)) return [];

        var result = new List<Commit>();
        foreach (var rawRecord in text.Split(RecordSeparator))
        {
            var record = rawRecord.TrimStart('\n');
            if (string.IsNullOrWhiteSpace(record)) continue;

            var fields = record.Split(UnitSeparator);
            if (fields.Length < LogFieldOrder.Length)
            {
                var padded = new string[LogFieldOrder.Length];
                Array.Copy(fields, padded, fields.Length);
                for (var i = fields.Length; i < padded.Length; i++) padded[i] = string.Empty;
                fields = padded;
            }

            var parents = fields[2]
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var authoredUnix = long.TryParse(fields[5], out var aSeconds) ? aSeconds : 0L;
            var committedUnix = long.TryParse(fields[6], out var cSeconds) ? cSeconds : 0L;
            var authored = DateTimeOffset.FromUnixTimeSeconds(authoredUnix);
            var committed = DateTimeOffset.FromUnixTimeSeconds(committedUnix);
            var bodyRaw = fields[8].TrimEnd('\n');
            var body = string.IsNullOrEmpty(bodyRaw) ? null : bodyRaw;

            result.Add(new Commit
            {
                Hash = fields[0],
                ShortHash = fields[1],
                AuthorName = fields[3],
                AuthorEmail = fields[4],
                AuthoredAt = authored,
                CommittedAt = committed,
                RelativeTime = RelativeTime.Describe(authored),
                Subject = fields[7],
                Body = body,
                Parents = parents,
            });
        }
        return result;
    }
}