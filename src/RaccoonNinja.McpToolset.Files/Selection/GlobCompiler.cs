using System.Text;
using System.Text.RegularExpressions;

namespace RaccoonNinja.McpToolset.Files.Selection;

/// <summary>
/// Compiles a glob pattern into an anchored <see cref="Regex"/> so the whole toolset matches paths
/// through a single, culture-invariant engine instead of a separate glob library. Translating rather
/// than expanding means brace groups (<c>{a,b}</c>) become linear regex alternation with no
/// combinatorial blow-up. Semantics: <c>*</c> matches within one path segment, <c>**</c> spans
/// segments, <c>**/</c> also matches zero directories (so <c>**/foo</c> matches <c>foo</c> at the
/// root), a pattern with no <c>/</c> matches the basename at any depth, and matching is
/// case-insensitive unless told otherwise.
/// </summary>
public static class GlobCompiler
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    /// <summary>The maximum glob length, mirroring the raw-regex pattern cap so translation stays bounded.</summary>
    private const int MaxGlobLength = 2048;

    /// <summary>The maximum brace-group nesting depth; deeper nesting is rejected before it can recurse.</summary>
    private const int MaxBraceDepth = 4;

    /// <summary>Compile <paramref name="glob"/> to an anchored regex over a <c>/</c>-separated root-relative path.</summary>
    /// <param name="glob">The glob pattern.</param>
    /// <param name="caseSensitive">When <c>true</c>, match case-sensitively; otherwise case-insensitively (the default).</param>
    /// <returns>A compiled regex that matches a whole path against <paramref name="glob"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="glob"/> is <c>null</c>.</exception>
    public static Regex Compile(string glob, bool caseSensitive = false)
    {
        ArgumentNullException.ThrowIfNull(glob);

        if (glob.Length > MaxGlobLength)
        {
            throw new RegexCompilationException($"glob exceeds the {MaxGlobLength}-character limit");
        }

        var body = Translate(glob, 0, glob.Length, depth: 0);
        var anchored = glob.Contains('/')
            ? string.Concat("^", body, "$")
            : string.Concat("^(?:.*/)?", body, "$");

        var options = RegexOptions.CultureInvariant;
        if (!caseSensitive)
        {
            options |= RegexOptions.IgnoreCase;
        }

        return new Regex(anchored, options, MatchTimeout);
    }

    /// <summary>Translate the glob span <c>[start, end)</c> into a regex fragment (recursively, for brace alternatives).</summary>
    private static string Translate(string s, int start, int end, int depth)
    {
        var sb = new StringBuilder();
        var i = start;
        while (i < end)
        {
            switch (s[i])
            {
                case '*':
                    i = AppendStar(s, i, end, sb);
                    break;
                case '?':
                    sb.Append("[^/]");
                    i++;
                    break;
                case '[':
                    i = AppendCharClass(s, i, end, sb);
                    break;
                case '{':
                    i = AppendBraceGroup(s, i, end, sb, depth);
                    break;
                default:
                    sb.Append(Regex.Escape(s[i].ToString()));
                    i++;
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>Emit <c>[^/]*</c> for <c>*</c>, <c>.*</c> for <c>**</c>, and <c>(?:.*/)?</c> for a <c>**/</c> segment.</summary>
    private static int AppendStar(string s, int i, int end, StringBuilder sb)
    {
        var isDouble = i + 1 < end && s[i + 1] == '*';
        if (!isDouble)
        {
            sb.Append("[^/]*");
            return i + 1;
        }

        if (i + 2 < end && s[i + 2] == '/')
        {
            sb.Append("(?:.*/)?");
            return i + 3;
        }

        sb.Append(".*");
        return i + 2;
    }

    /// <summary>Copy a <c>[...]</c> class, turning a leading <c>!</c> or <c>^</c> into regex negation and escaping class metacharacters.</summary>
    private static int AppendCharClass(string s, int i, int end, StringBuilder sb)
    {
        var close = s.IndexOf(']', i + 1);
        if (close < 0 || close >= end)
        {
            sb.Append(Regex.Escape("["));
            return i + 1;
        }

        sb.Append('[');
        var j = i + 1;
        if (j < close && (s[j] == '!' || s[j] == '^'))
        {
            sb.Append('^');
            j++;
        }

        while (j < close)
        {
            var ch = s[j];
            if (ch is '\\' or '^' or ']')
            {
                sb.Append('\\');
            }

            sb.Append(ch);
            j++;
        }

        sb.Append(']');
        return close + 1;
    }

    /// <summary>Turn a balanced <c>{a,b,...}</c> group into <c>(?:a|b|...)</c>, translating each top-level alternative.</summary>
    private static int AppendBraceGroup(string s, int i, int end, StringBuilder sb, int depth)
    {
        if (depth >= MaxBraceDepth)
        {
            throw new RegexCompilationException($"glob brace nesting exceeds the depth limit of {MaxBraceDepth}");
        }

        var close = FindMatchingBrace(s, i, end);
        if (close < 0)
        {
            sb.Append(Regex.Escape("{"));
            return i + 1;
        }

        var alternatives = SplitTopLevel(s, i + 1, close);
        sb.Append("(?:");
        for (var a = 0; a < alternatives.Count; a++)
        {
            if (a > 0)
            {
                sb.Append('|');
            }

            var (altStart, altEnd) = alternatives[a];
            sb.Append(Translate(s, altStart, altEnd, depth + 1));
        }

        sb.Append(')');
        return close + 1;
    }

    /// <summary>Find the index of the <c>}</c> that closes the <c>{</c> at <paramref name="openIdx"/>, or -1 when unbalanced.</summary>
    private static int FindMatchingBrace(string s, int openIdx, int end)
    {
        var depth = 0;
        for (var k = openIdx; k < end; k++)
        {
            if (s[k] == '{')
            {
                depth++;
            }
            else if (s[k] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return k;
                }
            }
        }

        return -1;
    }

    /// <summary>Split the span <c>[start, end)</c> on commas that sit at brace depth zero.</summary>
    private static List<(int Start, int End)> SplitTopLevel(string s, int start, int end)
    {
        var parts = new List<(int, int)>();
        var depth = 0;
        var segmentStart = start;
        for (var k = start; k < end; k++)
        {
            switch (s[k])
            {
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    break;
                case ',' when depth == 0:
                    parts.Add((segmentStart, k));
                    segmentStart = k + 1;
                    break;
                default:
                    break;
            }
        }

        parts.Add((segmentStart, end));
        return parts;
    }
}