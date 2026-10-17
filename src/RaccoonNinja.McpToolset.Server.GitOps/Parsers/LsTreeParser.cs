using System.Globalization;
using RaccoonNinja.McpToolset.Server.GitOps.Models;

namespace RaccoonNinja.McpToolset.Server.GitOps.Parsers;

/// <summary>Parser for <c>git ls-tree -z -l</c> output.</summary>
public static class LsTreeParser
{
    /// <summary>
    /// Parse NUL-terminated <c>&lt;mode&gt; &lt;type&gt; &lt;object&gt; &lt;size&gt;\t&lt;path&gt;</c> records. The
    /// size column is space-padded and reads <c>-</c> for non-blobs or <c>BAD</c> for an unreadable
    /// object; both become a <c>null</c> size. A malformed record is skipped.
    /// </summary>
    /// <param name="raw">The raw stdout bytes.</param>
    /// <returns>The parsed entries in git's order.</returns>
    public static IReadOnlyList<LsTreeEntry> Parse(byte[] raw)
    {
        var text = TextDecoding.Decode(raw);
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var entries = new List<LsTreeEntry>();
        foreach (var record in text.Split('\0'))
        {
            var tab = record.IndexOf('\t');
            if (tab < 0)
            {
                continue;
            }

            var meta = record[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (meta.Length < 4)
            {
                continue;
            }

            long? size = long.TryParse(meta[3], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
            entries.Add(new LsTreeEntry(meta[0], meta[1], meta[2], size, record[(tab + 1)..]));
        }

        return entries;
    }
}