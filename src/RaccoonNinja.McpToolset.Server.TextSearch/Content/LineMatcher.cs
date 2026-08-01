using System.Text.RegularExpressions;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Content;

/// <summary>
/// Finds the spans of a pattern within a single line. Matching is per line so <c>column</c> is a
/// direct line offset and a match can never straddle a newline. A regex matcher can throw
/// <see cref="RegexMatchTimeoutException"/> while enumerating; the caller catches it per file.
/// </summary>
public abstract class LineMatcher
{
    /// <summary>Yield each non-empty match span in <paramref name="line"/> as a 0-based start and length.</summary>
    /// <param name="line">The line text to search.</param>
    /// <returns>The match spans, left to right, non-overlapping.</returns>
    public abstract IEnumerable<(int Start, int Length)> Matches(string line);

    /// <summary>A matcher over a compiled regex, applied per line.</summary>
    /// <param name="regex">The compiled regex (carries its own match timeout).</param>
    /// <returns>The matcher.</returns>
    public static LineMatcher ForRegex(Regex regex) => new RegexLineMatcher(regex);

    /// <summary>A matcher over a literal substring.</summary>
    /// <param name="needle">The literal to find; must be non-empty.</param>
    /// <param name="caseSensitive">Whether matching is case-sensitive.</param>
    /// <returns>The matcher.</returns>
    public static LineMatcher ForLiteral(string needle, bool caseSensitive)
        => new LiteralLineMatcher(needle, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    private sealed class RegexLineMatcher(Regex regex) : LineMatcher
    {
        public override IEnumerable<(int Start, int Length)> Matches(string line)
        {
            foreach (Match match in regex.Matches(line))
            {
                // Skip zero-width matches so a pattern like a* does not report one hit per position.
                if (match.Length > 0)
                {
                    yield return (match.Index, match.Length);
                }
            }
        }
    }

    private sealed class LiteralLineMatcher(string needle, StringComparison comparison) : LineMatcher
    {
        public override IEnumerable<(int Start, int Length)> Matches(string line)
        {
            var from = 0;
            while (from <= line.Length - needle.Length)
            {
                var found = line.IndexOf(needle, from, comparison);
                if (found < 0)
                {
                    yield break;
                }

                yield return (found, needle.Length);
                from = found + needle.Length;
            }
        }
    }
}