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

            var path = parts[0];
            if (hasRef)
            {
                var colon = path.IndexOf(':');
                if (colon >= 0)
                {
                    path = path[(colon + 1)..];
                }
            }

            matches.Add(new GrepMatch
            {
                Path = path,
                Line = lineNo,
                Content = parts[2].TrimEnd('\r'),
            });
        }

        return matches;
    }
}