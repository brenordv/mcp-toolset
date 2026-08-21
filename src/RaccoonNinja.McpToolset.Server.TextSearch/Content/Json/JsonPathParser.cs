using System.Globalization;
using System.Text;
using RaccoonNinja.McpToolset.Server.TextSearch.Errors;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Content.Json;

/// <summary>
/// Parses the agent-supplied <c>json_path</c> string into <see cref="JsonPathSegment"/>s. The grammar
/// is deliberately minimal: dot-separated property names, bracket indexes with Python-style negatives
/// (<c>items[-1]</c>), and bracket-quoted names for keys the dot form cannot spell
/// (<c>deps["lodash.merge"]</c>, single or double quotes, escapes <c>\"</c> <c>\'</c> <c>\\</c>).
/// A leading <c>$</c> in root position is tolerated and ignored. The whole string is validated and
/// capped before any file is touched, so a malformed or oversized path can never become an escaped
/// exception; every rejection is an <see cref="ErrorCodes.InvalidArgument"/> whose message carries
/// only a character offset, never path content.
/// </summary>
internal static class JsonPathParser
{
    /// <summary>The maximum accepted <c>json_path</c> length, in characters.</summary>
    internal const int MaxLength = 1024;

    /// <summary>The maximum accepted number of segments.</summary>
    internal const int MaxSegments = 128;

    /// <summary>Parse <paramref name="jsonPath"/> into segments; blank means the document root.</summary>
    /// <param name="jsonPath">The raw <c>json_path</c> argument.</param>
    /// <returns>The parsed segments, root-first; empty for the document root.</returns>
    /// <exception cref="TextSearchException">Thrown as <see cref="ErrorCodes.InvalidArgument"/> when the path is malformed or over a cap.</exception>
    public static IReadOnlyList<JsonPathSegment> Parse(string jsonPath)
    {
        if (string.IsNullOrWhiteSpace(jsonPath))
        {
            return [];
        }

        if (jsonPath.Length > MaxLength)
        {
            throw TextSearchException.InvalidArgument($"json_path must not be longer than {MaxLength} characters");
        }

        var segments = new List<JsonPathSegment>();
        var pos = 0;

        // A '$' counts as the JSONPath root marker only in root position followed by '.', '[', or the
        // end; anywhere else it is an ordinary name character, so a literal "$" key stays reachable.
        if (jsonPath[0] == '$' && (jsonPath.Length == 1 || jsonPath[1] is '.' or '['))
        {
            pos = 1;
        }

        while (pos < jsonPath.Length)
        {
            var c = jsonPath[pos];
            if (c == '.')
            {
                if (pos == 0)
                {
                    throw InvalidAt(pos, "a path cannot start with '.'");
                }

                pos++;
                segments.Add(ParseName(jsonPath, ref pos));
            }
            else if (c == '[')
            {
                pos++;
                segments.Add(ParseBracket(jsonPath, ref pos));
            }
            else if (segments.Count == 0)
            {
                segments.Add(ParseName(jsonPath, ref pos));
            }
            else
            {
                throw InvalidAt(pos, "expected '.' or '['");
            }

            if (segments.Count > MaxSegments)
            {
                throw TextSearchException.InvalidArgument($"json_path must not have more than {MaxSegments} segments");
            }
        }

        return segments;
    }

    /// <summary>Parse an unquoted property name starting at <paramref name="pos"/>.</summary>
    private static JsonPathSegment ParseName(string jsonPath, ref int pos)
    {
        var start = pos;
        while (pos < jsonPath.Length && jsonPath[pos] is not ('.' or '[' or ']'))
        {
            pos++;
        }

        return pos == start
            ? throw InvalidAt(start, "expected a property name")
            : JsonPathSegment.ForProperty(jsonPath[start..pos]);
    }

    /// <summary>Parse a bracket form (a quoted name or an integer index); <paramref name="pos"/> starts after the <c>[</c>.</summary>
    private static JsonPathSegment ParseBracket(string jsonPath, ref int pos)
    {
        if (pos >= jsonPath.Length)
        {
            throw InvalidAt(pos, "unterminated '['");
        }

        var segment = jsonPath[pos] is '"' or '\''
            ? ParseQuotedName(jsonPath, ref pos)
            : ParseIndex(jsonPath, ref pos);

        if (pos >= jsonPath.Length || jsonPath[pos] != ']')
        {
            throw InvalidAt(pos, "expected ']'");
        }

        pos++;
        return segment;
    }

    /// <summary>Parse a quoted name inside brackets; <paramref name="pos"/> starts on the opening quote.</summary>
    private static JsonPathSegment ParseQuotedName(string jsonPath, ref int pos)
    {
        var quote = jsonPath[pos];
        pos++;
        var name = new StringBuilder();
        while (true)
        {
            if (pos >= jsonPath.Length)
            {
                throw InvalidAt(pos, "unterminated quoted name");
            }

            var c = jsonPath[pos];
            if (c == quote)
            {
                pos++;
                return JsonPathSegment.ForProperty(name.ToString());
            }

            if (c == '\\')
            {
                if (pos + 1 >= jsonPath.Length || jsonPath[pos + 1] is not ('\\' or '"' or '\''))
                {
                    throw InvalidAt(pos, "unsupported escape (only \\\\, \\\", and \\' are recognized)");
                }

                name.Append(jsonPath[pos + 1]);
                pos += 2;
                continue;
            }

            name.Append(c);
            pos++;
        }
    }

    /// <summary>Parse an integer index inside brackets; negatives are allowed (Python style).</summary>
    private static JsonPathSegment ParseIndex(string jsonPath, ref int pos)
    {
        var start = pos;
        if (jsonPath[pos] == '-')
        {
            pos++;
        }

        while (pos < jsonPath.Length && char.IsAsciiDigit(jsonPath[pos]))
        {
            pos++;
        }

        var digits = jsonPath[pos - 1] == '-' || pos == start
            ? throw InvalidAt(start, "an index must be an integer")
            : jsonPath[start..pos];

        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
            ? JsonPathSegment.ForIndex(index)
            : throw InvalidAt(start, "index is out of the supported integer range");
    }

    /// <summary>Build the invalid-argument error for offset <paramref name="pos"/> with a static reason.</summary>
    private static TextSearchException InvalidAt(int pos, string reason)
        => TextSearchException.InvalidArgument(
            string.Create(CultureInfo.InvariantCulture, $"json_path is invalid at offset {pos}: {reason}"));
}