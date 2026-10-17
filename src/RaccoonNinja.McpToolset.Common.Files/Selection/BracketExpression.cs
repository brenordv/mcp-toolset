using System.Text;
using System.Text.RegularExpressions;

namespace RaccoonNinja.McpToolset.Common.Files.Selection;

/// <summary>
/// Translates a glob bracket expression (<c>[...]</c>) into a .NET character class. Shared by
/// <see cref="IgnoreRule"/> and <see cref="GlobCompiler"/> so both read brackets the same way git's
/// matcher does: a leading <c>!</c> or <c>^</c> negates, and a <c>]</c> in first position (after any
/// negation) is a literal member rather than the closing bracket.
/// </summary>
internal static class BracketExpression
{
    /// <summary>
    /// Translate the bracket expression that opens at <paramref name="s"/>[<paramref name="openIndex"/>]
    /// into a .NET character class appended to <paramref name="sb"/>. Inside the class, <c>\</c>,
    /// <c>^</c>, <c>]</c>, and <c>[</c> are escaped; escaping <c>[</c> keeps .NET's class-subtraction
    /// syntax (<c>[a-z-[aeiou]]</c>) out of reach, since git reads that bracket literally.
    /// </summary>
    /// <param name="s">The pattern text.</param>
    /// <param name="openIndex">The index of the opening <c>[</c>.</param>
    /// <param name="limit">The exclusive end of the span the closing bracket must fall inside.</param>
    /// <param name="sb">The regex being built.</param>
    /// <returns>
    /// The index just past the closing bracket, or <paramref name="openIndex"/> + 1 when the expression
    /// is unterminated (the <c>[</c> is then emitted as a literal).
    /// </returns>
    public static int Append(string s, int openIndex, int limit, StringBuilder sb)
    {
        var j = openIndex + 1;
        var negated = false;
        if (j < limit && (s[j] == '!' || s[j] == '^'))
        {
            negated = true;
            j++;
        }

        var searchFrom = j < limit && s[j] == ']' ? j + 1 : j;
        var close = searchFrom < limit ? s.IndexOf(']', searchFrom) : -1;
        if (close < 0 || close >= limit)
        {
            sb.Append(Regex.Escape("["));
            return openIndex + 1;
        }

        sb.Append('[');
        if (negated)
        {
            sb.Append('^');
        }

        for (var k = j; k < close; k++)
        {
            var ch = s[k];
            if (ch is '\\' or '^' or ']' or '[')
            {
                sb.Append('\\');
            }

            sb.Append(ch);
        }

        sb.Append(']');
        return close + 1;
    }
}