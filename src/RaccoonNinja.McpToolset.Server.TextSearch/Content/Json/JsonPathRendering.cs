using System.Buffers;
using System.Globalization;
using System.Text;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Content.Json;

/// <summary>
/// Renders parsed <see cref="JsonPathSegment"/>s back into canonical <c>json_path</c> text, used to
/// report the deepest resolved prefix on a miss. Names the dot form cannot spell come back
/// bracket-quoted, so the rendered prefix is always re-parseable.
/// </summary>
internal static class JsonPathRendering
{
    private const string Root = "$";

    private static readonly SearchValues<char> s_dotFormBreakers = SearchValues.Create(".[]\"'\\");

    /// <summary>Render the first <paramref name="count"/> of <paramref name="segments"/>; zero renders the root marker.</summary>
    /// <param name="segments">The parsed segments.</param>
    /// <param name="count">How many leading segments to render.</param>
    /// <returns>The canonical path text (<c>$</c> for the root).</returns>
    public static string Render(IReadOnlyList<JsonPathSegment> segments, int count)
    {
        ArgumentNullException.ThrowIfNull(segments);

        if (count == 0)
        {
            return Root;
        }

        var text = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            var segment = segments[i];
            if (segment.IsIndex)
            {
                text.Append('[').Append(segment.Index.ToString(CultureInfo.InvariantCulture)).Append(']');
            }
            else if (NeedsQuoting(segment.Property))
            {
                text.Append("[\"").Append(Escape(segment.Property)).Append("\"]");
            }
            else
            {
                if (text.Length > 0)
                {
                    text.Append('.');
                }

                text.Append(segment.Property);
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// Whether <paramref name="name"/> cannot be spelled in the dot form. A name that is exactly
    /// <c>$</c> is quoted too: unquoted in root position it would re-parse as the root marker.
    /// </summary>
    private static bool NeedsQuoting(string name)
        => name.Length == 0
            || name == Root
            || name.AsSpan().ContainsAny(s_dotFormBreakers)
            || char.IsWhiteSpace(name[0])
            || char.IsWhiteSpace(name[^1]);

    /// <summary>Escape backslashes and double quotes for the quoted form.</summary>
    private static string Escape(string name)
        => name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}