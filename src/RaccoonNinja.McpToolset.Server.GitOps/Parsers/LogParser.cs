using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Models;

namespace RaccoonNinja.McpToolset.Server.GitOps.Parsers;

/// <summary>
/// Parser for <c>git log -z --pretty=tformat:%H%x00%h%x00...%x00%b</c> output: fields separated by NUL, each record ended
/// by NUL (see <see cref="NulRecords"/>).
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

    /// <summary>How many leading fields name something: the hash, the short hash, and the parents.</summary>
    private const int IdentifyingFieldCount = 3;

    /// <summary>
    /// The git pretty-format spec corresponding to <see cref="LogFieldOrder"/>, its fields separated by NUL (<c>%x00</c>).
    /// It carries no <c>%G</c> placeholder: those verify the commit's signature by running the program <c>gpg.program</c>
    /// names, whatever <c>log.showSignature</c> says, and a repository's own config can name that program.
    /// </summary>
    public const string LogPrettyFormat = "%H%x00%h%x00%P%x00%an%x00%ae%x00%at%x00%ct%x00%s%x00%b";

    /// <summary>
    /// The flags every <c>git log</c> intent <see cref="Parse"/> reads passes: <c>-z</c>, which makes git end each record
    /// with NUL, and <see cref="LogPrettyFormat"/> as <c>tformat:</c>, which ends every record, the last one included.
    /// </summary>
    public static readonly IReadOnlyList<string> FormatFlags = ["-z", $"--pretty=tformat:{LogPrettyFormat}"];

    /// <summary>Parse the output of a <c>git log</c> run with <see cref="FormatFlags"/>.</summary>
    /// <param name="raw">git's standard output.</param>
    /// <param name="truncated">
    /// Whether the output hit the output cap; a cut last record comes back with its missing fields empty, or is left out
    /// when the cut may fall inside its hash, short hash, or parents.
    /// </param>
    /// <returns>The commits, in git's order.</returns>
    /// <exception cref="GitCommandException">The output was not cut and is not a whole number of records.</exception>
    public static IReadOnlyList<Commit> Parse(byte[] raw, bool truncated)
        => [.. NulRecords.LeaveOutCutIdentifiers(NulRecords.Split(raw, LogFieldOrder.Length, truncated), IdentifyingFieldCount, truncated).Select(ToCommit)];

    /// <summary>Build a <see cref="Commit"/> from one record's fields, in <see cref="LogFieldOrder"/>.</summary>
    /// <param name="fields">The record's fields.</param>
    /// <returns>The commit.</returns>
    private static Commit ToCommit(string[] fields)
    {
        var parents = fields[2]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var authoredUnix = long.TryParse(fields[5], out var aSeconds) ? aSeconds : 0L;
        var committedUnix = long.TryParse(fields[6], out var cSeconds) ? cSeconds : 0L;
        var authored = DateTimeOffset.FromUnixTimeSeconds(authoredUnix);
        var committed = DateTimeOffset.FromUnixTimeSeconds(committedUnix);
        var bodyRaw = fields[8].TrimEnd('\n');
        var body = string.IsNullOrWhiteSpace(bodyRaw) ? null : bodyRaw;

        return new Commit
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
        };
    }
}