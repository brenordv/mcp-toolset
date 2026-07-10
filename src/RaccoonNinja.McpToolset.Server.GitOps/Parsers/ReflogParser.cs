using RaccoonNinja.McpToolset.Server.GitOps.Models;

namespace RaccoonNinja.McpToolset.Server.GitOps.Parsers;

/// <summary>
/// Parser for <c>git reflog show --format=%H%x1f%h%x1f%gd%x1f%gs%x1f%at</c> output.
/// Fields are separated by Unit Separator (US, 0x1F); entries by newline.
/// </summary>
public static class ReflogParser
{
    /// <summary>The git format spec this parser expects, in <see cref="ReflogEntry"/> field order.</summary>
    public const string ReflogFormat = "%H%x1f%h%x1f%gd%x1f%gs%x1f%at";

    private const char UnitSeparator = '\x1f';
    private const int FieldCount = 5;

    /// <summary>Parse the reflog output into ordered <see cref="ReflogEntry"/> records.</summary>
    /// <param name="raw">Raw stdout bytes from <c>git reflog</c>.</param>
    /// <returns>The parsed entries, or an empty list when there is nothing to parse.</returns>
    public static IReadOnlyList<ReflogEntry> Parse(byte[] raw)
    {
        var text = TextDecoding.Decode(raw);
        var entries = new List<ReflogEntry>();
        foreach (var line in text.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split(UnitSeparator);
            if (parts.Length < FieldCount)
            {
                continue;
            }

            var when = long.TryParse(parts[4], out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : DateTimeOffset.FromUnixTimeSeconds(0);

            entries.Add(new ReflogEntry
            {
                Hash = parts[0],
                ShortHash = parts[1],
                Selector = parts[2],
                Subject = parts[3],
                When = when,
                RelativeTime = RelativeTime.Describe(when),
            });
        }

        return entries;
    }
}