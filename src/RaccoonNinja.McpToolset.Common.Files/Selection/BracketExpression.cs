using System.Text;
using System.Text.RegularExpressions;

namespace RaccoonNinja.McpToolset.Common.Files.Selection;

/// <summary>
/// Translates a glob bracket expression (<c>[...]</c>) into a .NET character class. Shared by
/// <see cref="IgnoreRule"/> and <see cref="GlobCompiler"/> so both read brackets the same way git's
/// matcher does: a leading <c>!</c> or <c>^</c> negates, a <c>]</c> in first position (after any
/// negation) is a literal member rather than the closing bracket, and no bracket expression, negated
/// or not, ever matches <c>/</c>. A range's low end is a member in its own right, the character after
/// a range cannot start another range (<c>[a-c-e]</c> is <c>a</c>..<c>c</c>, <c>-</c>, <c>e</c>), and a
/// backslash or <c>[</c> inside the brackets is a literal member.
/// </summary>
internal static class BracketExpression
{
    /// <summary>
    /// Translate the bracket expression that opens at <paramref name="s"/>[<paramref name="openIndex"/>]
    /// into a .NET character class appended to <paramref name="sb"/>, prefixed with <c>(?!/)</c> so the
    /// class never matches <c>/</c>. A reversed range such as <c>[z-a]</c> keeps only its low character
    /// (<c>z</c>), as in git, unless <paramref name="rejectReversedRange"/> asks for it to be rejected.
    /// </summary>
    /// <param name="s">The pattern text.</param>
    /// <param name="openIndex">The index of the opening <c>[</c>.</param>
    /// <param name="limit">The exclusive end of the span the closing bracket must fall inside.</param>
    /// <param name="sb">The regex being built.</param>
    /// <param name="rejectReversedRange">
    /// When <c>true</c>, a reversed range is emitted as written so the regex parser rejects it; when
    /// <c>false</c>, it contributes only its low character, as in git.
    /// </param>
    /// <returns>
    /// The index just past the closing bracket, or <paramref name="openIndex"/> + 1 when the expression
    /// is unterminated (the <c>[</c> is then emitted as a literal).
    /// </returns>
    public static int Append(string s, int openIndex, int limit, StringBuilder sb, bool rejectReversedRange)
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

        sb.Append("(?!/)[");
        if (negated)
        {
            sb.Append('^');
        }

        char? prev = null;
        var k = j;
        while (k < close)
        {
            var ch = s[k];
            if (ch == '-' && prev is { } low && k + 1 < close)
            {
                var high = s[k + 1];
                if (low <= high || rejectReversedRange)
                {
                    sb.Append('-');
                    AppendMember(sb, high);
                }

                prev = null;
                k += 2;
                continue;
            }

            AppendMember(sb, ch);
            prev = ch;
            k++;
        }

        sb.Append(']');
        return close + 1;
    }

    /// <summary>
    /// Append one class member: <c>-</c> as <c>\x2D</c> (a literal dash that can still be a range end),
    /// and <c>\</c>, <c>^</c>, <c>]</c>, <c>[</c> backslash-escaped; escaping <c>[</c> keeps .NET's
    /// class-subtraction syntax (<c>[a-z-[aeiou]]</c>) out of reach, since git reads that bracket literally.
    /// </summary>
    private static void AppendMember(StringBuilder sb, char c)
    {
        if (c == '-')
        {
            sb.Append(@"\x2D");
            return;
        }

        if (c is '\\' or '^' or ']' or '[')
        {
            sb.Append('\\');
        }

        sb.Append(c);
    }
}