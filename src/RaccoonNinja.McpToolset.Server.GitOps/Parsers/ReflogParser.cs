using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Models;

namespace RaccoonNinja.McpToolset.Server.GitOps.Parsers;

/// <summary>
/// Parser for <c>git reflog show -z --format=tformat:%H%x00%h%x00%gd%x00%gs%x00%at</c> output: fields separated by NUL,
/// each entry ended by NUL (see <see cref="NulRecords"/>).
/// </summary>
public static class ReflogParser
{
    /// <summary>
    /// The flags this parser expects: <c>-z</c> and the format as <c>tformat:</c>, in <see cref="ReflogEntry"/> field order.
    /// </summary>
    public static readonly IReadOnlyList<string> FormatFlags = ["-z", "--format=tformat:%H%x00%h%x00%gd%x00%gs%x00%at"];

    private const int FieldCount = 5;

    /// <summary>Parse the output of a <c>git reflog show</c> run with <see cref="FormatFlags"/>.</summary>
    /// <param name="raw">Raw stdout bytes from <c>git reflog</c>.</param>
    /// <param name="truncated">Whether the output hit the output cap; a cut last entry comes back with its missing fields empty.</param>
    /// <returns>The parsed entries, or an empty list when there is nothing to parse.</returns>
    /// <exception cref="GitCommandException">The output was not cut and is not a whole number of entries.</exception>
    public static IReadOnlyList<ReflogEntry> Parse(byte[] raw, bool truncated)
        => [.. NulRecords.Split(raw, FieldCount, truncated).Select(ToEntry)];

    /// <summary>Build a <see cref="ReflogEntry"/> from one entry's fields.</summary>
    /// <param name="fields">The entry's fields, in <see cref="FormatFlags"/> order.</param>
    /// <returns>The entry.</returns>
    private static ReflogEntry ToEntry(string[] fields)
    {
        var when = long.TryParse(fields[4], out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : DateTimeOffset.FromUnixTimeSeconds(0);

        return new ReflogEntry
        {
            Hash = fields[0],
            ShortHash = fields[1],
            Selector = fields[2],
            Subject = fields[3],
            When = when,
            RelativeTime = RelativeTime.Describe(when),
        };
    }
}