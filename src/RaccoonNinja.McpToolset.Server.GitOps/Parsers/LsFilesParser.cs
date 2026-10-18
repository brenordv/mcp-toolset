using System.Globalization;
using RaccoonNinja.McpToolset.Server.GitOps.Models;

namespace RaccoonNinja.McpToolset.Server.GitOps.Parsers;

/// <summary>Parser for <c>git ls-files -z</c> output (NUL-separated path list), with or without <c>--stage</c>.</summary>
public static class LsFilesParser
{
    public static IReadOnlyList<string> Parse(byte[] raw)
    {
        var text = TextDecoding.Decode(raw);

        return string.IsNullOrWhiteSpace(text)
            ? []
            : [.. text.Split('\0').Where(path => !string.IsNullOrWhiteSpace(path))];
    }

    /// <summary>
    /// Parse <c>git ls-files -z --stage</c> output, one <c>&lt;mode&gt; &lt;object&gt; &lt;stage&gt;\t&lt;path&gt;</c>
    /// record per NUL. A malformed record is skipped; git never prints one, so it only appears when the
    /// output was cut, which the envelope's <c>truncated</c> flag already reports.
    /// </summary>
    /// <param name="raw">The raw stdout bytes.</param>
    /// <returns>The parsed entries in git's order.</returns>
    public static IReadOnlyList<LsFilesEntry> ParseStage(byte[] raw)
    {
        var text = TextDecoding.Decode(raw);
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var entries = new List<LsFilesEntry>();
        foreach (var record in text.Split('\0'))
        {
            var tab = record.IndexOf('\t');
            if (tab < 0)
            {
                continue;
            }

            var meta = record[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (meta.Length < 3 || !int.TryParse(meta[2], NumberStyles.None, CultureInfo.InvariantCulture, out var stage))
            {
                continue;
            }

            entries.Add(new LsFilesEntry
            {
                Path = record[(tab + 1)..],
                Mode = meta[0],
                ObjectId = meta[1],
                Stage = stage,
            });
        }

        return entries;
    }
}