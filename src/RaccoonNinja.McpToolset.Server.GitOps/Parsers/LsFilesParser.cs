using System.Globalization;
using RaccoonNinja.McpToolset.Server.GitOps.Models;

namespace RaccoonNinja.McpToolset.Server.GitOps.Parsers;

/// <summary>
/// Parser for <c>git ls-files -z</c> output (NUL-separated path list), with or without <c>--stage</c>. git ends every
/// record with NUL, so in output the output cap cut, the text after the last NUL is a record whose NUL never arrived; it
/// is left out, since a path cut short can be the whole name of another file.
/// </summary>
public static class LsFilesParser
{
    /// <summary>Parse <c>git ls-files -z</c> output, one path per NUL.</summary>
    /// <param name="raw">The raw stdout bytes.</param>
    /// <param name="truncated">Whether the output hit the output cap; the text after the last NUL is then left out.</param>
    /// <returns>The paths in git's order.</returns>
    public static IReadOnlyList<string> Parse(byte[] raw, bool truncated)
    {
        var text = TextDecoding.Decode(raw);

        return string.IsNullOrWhiteSpace(text)
            ? []
            : [.. WholeRecords(text, truncated).Where(path => !string.IsNullOrWhiteSpace(path))];
    }

    /// <summary>
    /// Parse <c>git ls-files -z --stage</c> output, one <c>&lt;mode&gt; &lt;object&gt; &lt;stage&gt;\t&lt;path&gt;</c>
    /// record per NUL. A malformed record is skipped; git never prints one.
    /// </summary>
    /// <param name="raw">The raw stdout bytes.</param>
    /// <param name="truncated">Whether the output hit the output cap; the text after the last NUL is then left out.</param>
    /// <returns>The parsed entries in git's order.</returns>
    public static IReadOnlyList<LsFilesEntry> ParseStage(byte[] raw, bool truncated)
    {
        var text = TextDecoding.Decode(raw);
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var entries = new List<LsFilesEntry>();
        foreach (var record in WholeRecords(text, truncated))
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

    /// <summary>Split <paramref name="text"/> on NUL, without the text after the last NUL when the output was cut.</summary>
    /// <param name="text">The decoded output.</param>
    /// <param name="truncated">Whether the output hit the output cap.</param>
    /// <returns>The records.</returns>
    private static string[] WholeRecords(string text, bool truncated)
    {
        var records = text.Split('\0');
        return truncated ? records[..^1] : records;
    }
}