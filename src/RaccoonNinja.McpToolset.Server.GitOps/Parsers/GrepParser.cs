using System.Globalization;
using RaccoonNinja.McpToolset.Server.GitOps.Models;

namespace RaccoonNinja.McpToolset.Server.GitOps.Parsers;

/// <summary>
/// Parser for <c>git grep -n -z</c> output. Each record is <c>&lt;path&gt;\0&lt;line&gt;\0&lt;content&gt;</c>;
/// when grepping a ref, git prefixes the path with <c>&lt;ref&gt;:</c>, which this parser strips.
/// </summary>
public static class GrepParser
{
    /// <summary>Parse the NUL-delimited grep output into ordered <see cref="GrepMatch"/> records.</summary>
    /// <param name="raw">Raw stdout bytes from <c>git grep</c>.</param>
    /// <param name="reference">The ref grepped at, if any; used to strip the <c>ref:</c> path prefix.</param>
    /// <returns>The parsed matches, or an empty list when there is nothing to parse.</returns>
    public static IReadOnlyList<GrepMatch> Parse(byte[] raw, string reference)
    {
        var text = TextDecoding.Decode(raw);
        var matches = new List<GrepMatch>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return matches;
        }

        var hasRef = !string.IsNullOrWhiteSpace(reference);
        foreach (var line in text.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split('\0', 3);
            if (parts.Length < 3)
            {
                continue;
            }

            if (!int.TryParse(parts[1], out var lineNo))
            {
                continue;
            }

            matches.Add(new GrepMatch
            {
                Path = StripRefPrefix(parts[0], hasRef),
                Line = lineNo,
                Content = parts[2].TrimEnd('\r'),
            });
        }

        return matches;
    }

    /// <summary>
    /// Parse <c>git grep -c -z</c> output: one <c>&lt;path&gt;\0&lt;count&gt;</c> record per line for
    /// each file with at least one matching line. A record with no NUL, or whose count does not parse, is
    /// skipped; git never prints one, so it only appears when the output was cut (which the envelope's
    /// truncated flag reports) or when a filename contains a newline.
    /// </summary>
    /// <param name="raw">Raw stdout bytes from <c>git grep -c -z</c>.</param>
    /// <param name="reference">The ref grepped at, if any; used to strip the <c>ref:</c> path prefix.</param>
    /// <returns>The parsed counts in git's order, or an empty list.</returns>
    public static IReadOnlyList<GrepFileCount> ParseCounts(byte[] raw, string reference)
    {
        var text = TextDecoding.Decode(raw);
        var counts = new List<GrepFileCount>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return counts;
        }

        var hasRef = !string.IsNullOrWhiteSpace(reference);
        foreach (var line in text.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var nul = line.LastIndexOf('\0');
            if (nul < 0)
            {
                continue;
            }

            if (!int.TryParse(line[(nul + 1)..].TrimEnd('\r'), NumberStyles.None, CultureInfo.InvariantCulture, out var count))
            {
                continue;
            }

            counts.Add(new GrepFileCount
            {
                Path = StripRefPrefix(line[..nul], hasRef),
                MatchCount = count,
            });
        }

        return counts;
    }

    /// <summary>Strip the <c>&lt;ref&gt;:</c> prefix git puts on every path when grepping a ref.</summary>
    /// <param name="path">The path as git printed it.</param>
    /// <param name="hasRef">Whether the grep ran at a ref.</param>
    /// <returns>The path without the prefix, or <paramref name="path"/> unchanged when no ref was grepped.</returns>
    private static string StripRefPrefix(string path, bool hasRef)
    {
        if (!hasRef)
        {
            return path;
        }

        var colon = path.IndexOf(':');
        return colon >= 0 ? path[(colon + 1)..] : path;
    }
}