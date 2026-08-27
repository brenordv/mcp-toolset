using System.Text;
using System.Text.Json;

namespace RaccoonNinja.McpToolset.Common.Mcp;

/// <summary>
/// Pure argument-name validation against a tool's JSON input schema. It compares the supplied names to
/// the schema's <c>properties</c> and <c>required</c> lists and reports the unknown and missing names,
/// each unknown carrying a best-effort suggestion. No I/O and no SDK types beyond
/// <see cref="JsonElement"/>, so it is unit-tested directly. The input schema is trusted, server-defined
/// data: each server passes its own tool's input schema, and the suggestion search runs an edit-distance
/// pass over the schema's property names, so a caller feeding an untrusted schema with very long names
/// owns that cost.
/// </summary>
public static class ArgumentShapeValidator
{
    /// <summary>The most unknown keys echoed back; any beyond this are still rejected but not named.</summary>
    private const int MaxEchoedUnknown = 5;

    /// <summary>The most UTF-16 code units of an echoed key, always cut on a rune boundary.</summary>
    private const int MaxKeyLength = 64;

    /// <summary>The largest normalized edit distance that still yields a suggestion.</summary>
    private const int MaxSuggestionDistance = 2;

    /// <summary>Validate the supplied argument names against a tool's input schema.</summary>
    /// <param name="inputSchema">The tool's JSON input schema (an object with a <c>properties</c> map).</param>
    /// <param name="argumentNames">The names the caller supplied, in call order.</param>
    /// <returns>The validation result.</returns>
    public static ArgumentShapeResult Validate(JsonElement inputSchema, IReadOnlyList<string> argumentNames)
    {
        ArgumentNullException.ThrowIfNull(argumentNames);

        var expected = ReadProperties(inputSchema);
        var expectedSet = new HashSet<string>(expected, StringComparer.Ordinal);
        var required = ReadRequired(inputSchema);
        var supplied = new HashSet<string>(argumentNames, StringComparer.Ordinal);

        var missing = required.Where(name => !supplied.Contains(name)).ToArray();
        var unknownNames = argumentNames.Where(name => !expectedSet.Contains(name)).ToArray();

        var echoed = new List<ArgumentShapeUnknown>(Math.Min(unknownNames.Length, MaxEchoedUnknown));
        foreach (var raw in unknownNames.Take(MaxEchoedUnknown))
        {
            var given = Sanitize(raw);
            echoed.Add(new ArgumentShapeUnknown(given, Suggest(given, expected)));
        }

        return new ArgumentShapeResult
        {
            IsValid = unknownNames.Length == 0 && missing.Length == 0,
            MissingRequired = missing,
            Unknown = echoed,
            Expected = expected,
        };
    }

    private static List<string> ReadProperties(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object
            || !schema.TryGetProperty("properties", out var properties)
            || properties.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var names = new List<string>();
        foreach (var property in properties.EnumerateObject())
        {
            names.Add(property.Name);
        }

        return names;
    }

    private static List<string> ReadRequired(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object
            || !schema.TryGetProperty("required", out var required)
            || required.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var names = new List<string>();
        foreach (var item in required.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                names.Add(item.GetString());
            }
        }

        return names;
    }

    private static string Sanitize(string raw)
        => Truncate(StripControls(raw), MaxKeyLength);

    private static string StripControls(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch == ' ' || !char.IsControl(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private static string Truncate(string text, int maxUtf16)
    {
        if (text.Length <= maxUtf16)
        {
            return text;
        }

        var taken = 0;
        var builder = new StringBuilder(maxUtf16);
        foreach (var rune in text.EnumerateRunes())
        {
            if (taken + rune.Utf16SequenceLength > maxUtf16)
            {
                break;
            }

            builder.Append(rune.ToString());
            taken += rune.Utf16SequenceLength;
        }

        return builder.ToString();
    }

    private static string Suggest(string given, IReadOnlyList<string> expected)
    {
        var normalizedGiven = Normalize(given);
        string best = null;
        var bestDistance = int.MaxValue;
        foreach (var name in expected)
        {
            var normalizedName = Normalize(name);
            if (string.Equals(normalizedName, normalizedGiven, StringComparison.Ordinal))
            {
                return name;
            }

            var distance = Levenshtein(normalizedGiven, normalizedName);
            if (distance > MaxSuggestionDistance)
            {
                continue;
            }

            if (distance < bestDistance || (distance == bestDistance && (best is null || string.CompareOrdinal(name, best) < 0)))
            {
                best = name;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.ToLowerInvariant())
        {
            if (ch != '_' && ch != '-')
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private static int Levenshtein(string a, string b)
    {
        // Two-row dynamic-programming edit distance (insert, delete, substitute each cost 1).
        if (a.Length == 0)
        {
            return b.Length;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}