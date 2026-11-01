using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Models;

namespace RaccoonNinja.McpToolset.Server.GitOps.Parsers;

/// <summary>
/// Parser for <c>git stash list -z --format=tformat:%gd%x00%gs%x00%at</c> output: fields separated by NUL, each entry
/// ended by NUL (see <see cref="NulRecords"/>).
/// </summary>
public static class StashListParser
{
    /// <summary>The flags this parser expects: <c>-z</c> and the format (selector, subject, author time) as <c>tformat:</c>.</summary>
    public static readonly IReadOnlyList<string> FormatFlags = ["-z", "--format=tformat:%gd%x00%gs%x00%at"];

    private const int FieldCount = 3;

    /// <summary>
    /// Parse the output of a <c>git stash list</c> run with <see cref="FormatFlags"/>. An entry whose selector has no
    /// closing <c>}</c> is left out: only an output cut by the cap ends inside a selector, and such an entry's index would
    /// read as 0 and name another stash.
    /// </summary>
    /// <param name="raw">Raw stdout bytes from <c>git stash list</c>.</param>
    /// <param name="truncated">Whether the output hit the output cap; a cut last entry comes back with its missing fields empty.</param>
    /// <returns>The parsed entries, or an empty list when there is nothing to parse.</returns>
    /// <exception cref="GitCommandException">The output was not cut and is not a whole number of entries.</exception>
    public static IReadOnlyList<StashEntry> Parse(byte[] raw, bool truncated)
        => [.. NulRecords.Split(raw, FieldCount, truncated).Where(fields => fields[0].EndsWith('}')).Select(ToEntry)];

    /// <summary>Build a <see cref="StashEntry"/> from one entry's fields.</summary>
    /// <param name="fields">The entry's fields, in <see cref="FormatFlags"/> order.</param>
    /// <returns>The entry.</returns>
    private static StashEntry ToEntry(string[] fields)
    {
        var refStr = fields[0];
        var subject = fields[1];
        var ts = long.TryParse(fields[2], out var seconds) ? seconds : 0L;
        var idx = 0;
        if (refStr.StartsWith("stash@{", StringComparison.Ordinal) && refStr.EndsWith('}'))
        {
            var inner = refStr.Substring("stash@{".Length, refStr.Length - "stash@{".Length - 1);
            _ = int.TryParse(inner, out idx);
        }
        var when = DateTimeOffset.FromUnixTimeSeconds(ts);
        return new StashEntry
        {
            Index = idx,
            Name = refStr,
            Subject = subject,
            RelativeTime = RelativeTime.Describe(when),
        };
    }
}