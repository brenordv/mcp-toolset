using System.Text;
using System.Text.RegularExpressions;

namespace RaccoonNinja.McpToolset.Files.Selection;

/// <summary>
/// One compiled <c>.gitignore</c>/<c>.mcpignore</c> pattern: a regex over a <c>/</c>-separated
/// root-relative path plus the two flags that decide how it applies (negation and directory-only).
/// Internal on purpose; callers evaluate rules through <see cref="IgnoreRules"/> and read the original
/// pattern text back through <see cref="IgnoreRules.Patterns"/>.
/// </summary>
internal sealed class IgnoreRule
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private readonly Regex _regex;

    private IgnoreRule(string source, bool negated, bool directoryOnly, Regex regex)
    {
        Source = source;
        Negated = negated;
        DirectoryOnly = directoryOnly;
        _regex = regex;
    }

    /// <summary>The original pattern line, kept for <c>describe_scope</c> reporting.</summary>
    public string Source { get; }

    /// <summary>Whether this is a <c>!</c> re-include rule (flips a matched path back to not-ignored).</summary>
    public bool Negated { get; }

    /// <summary>Whether the pattern ended in <c>/</c> and so matches directories only.</summary>
    public bool DirectoryOnly { get; }

    /// <summary>
    /// Compile <paramref name="rawLine"/> into a rule anchored under <paramref name="basePath"/>, or
    /// return <c>null</c> when the line is blank or a comment and contributes no rule.
    /// </summary>
    /// <param name="rawLine">A single line from an ignore file.</param>
    /// <param name="basePath">The root-relative POSIX directory the ignore file lives in (<c>""</c> for the root).</param>
    public static IgnoreRule TryCompile(string rawLine, string basePath)
    {
        var line = Preprocess(rawLine, out var negated);
        if (line is null)
        {
            return null;
        }

        var directoryOnly = line.EndsWith('/');
        if (directoryOnly)
        {
            line = line[..^1];
        }

        if (line.Length == 0)
        {
            return null;
        }

        var anchored = line.Contains('/');
        if (line.StartsWith('/'))
        {
            line = line[1..];
        }

        var body = TranslateBody(line);
        var prefix = BuildPrefix(basePath);
        var pattern = anchored
            ? string.Concat("^", prefix, body, "$")
            : string.Concat("^", prefix, "(?:.*/)?", body, "$");

        var options = RegexOptions.CultureInvariant;
        if (!OperatingSystem.IsLinux())
        {
            options |= RegexOptions.IgnoreCase;
        }

        return new IgnoreRule(rawLine, negated, directoryOnly, new Regex(pattern, options, MatchTimeout));
    }

    /// <summary>Whether this rule matches <paramref name="relativePath"/> given whether it is a directory.</summary>
    public bool IsMatch(string relativePath, bool isDirectory)
    {
        if (DirectoryOnly && !isDirectory)
        {
            return false;
        }

        return _regex.IsMatch(relativePath);
    }

    /// <summary>Strip the trailing CR, comment lines, unescaped trailing whitespace, and a leading <c>!</c>.</summary>
    private static string Preprocess(string rawLine, out bool negated)
    {
        negated = false;
        if (rawLine is null)
        {
            return null;
        }

        var line = rawLine.EndsWith('\r') ? rawLine[..^1] : rawLine;
        line = StripTrailingWhitespace(line);
        if (line.Length == 0 || line[0] == '#')
        {
            return null;
        }

        if (line[0] == '!')
        {
            negated = true;
            line = line[1..];
        }

        return line.Length == 0 ? null : line;
    }

    /// <summary>Remove trailing spaces/tabs, but keep one that a trailing backslash escapes.</summary>
    private static string StripTrailingWhitespace(string line)
    {
        var end = line.Length;
        while (end > 0 && (line[end - 1] == ' ' || line[end - 1] == '\t'))
        {
            end--;
        }

        if (end < line.Length && end > 0 && line[end - 1] == '\\')
        {
            // The backslash escapes the first stripped space: keep one literal space (translation drops the backslash).
            return string.Concat(line.AsSpan(0, end), " ");
        }

        return line[..end];
    }

    /// <summary>Turn the pattern body (no anchoring markers) into a regex fragment over a <c>/</c>-separated path.</summary>
    private static string TranslateBody(string p)
    {
        var sb = new StringBuilder();
        var i = 0;
        while (i < p.Length)
        {
            switch (p[i])
            {
                case '*':
                    i = AppendStar(p, i, sb);
                    break;
                case '?':
                    sb.Append("[^/]");
                    i++;
                    break;
                case '[':
                    i = AppendCharClass(p, i, sb);
                    break;
                case '\\':
                    i = AppendEscaped(p, i, sb);
                    break;
                default:
                    sb.Append(Regex.Escape(p[i].ToString()));
                    i++;
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>Emit <c>[^/]*</c> for <c>*</c>, <c>.*</c> for <c>**</c>, and <c>(?:.*/)?</c> for a <c>**/</c> run.</summary>
    private static int AppendStar(string s, int i, StringBuilder sb)
    {
        var isDouble = i + 1 < s.Length && s[i + 1] == '*';
        if (!isDouble)
        {
            sb.Append("[^/]*");
            return i + 1;
        }

        if (i + 2 < s.Length && s[i + 2] == '/')
        {
            sb.Append("(?:.*/)?");
            return i + 3;
        }

        sb.Append(".*");
        return i + 2;
    }

    /// <summary>Copy a <c>[...]</c> class, turning a leading <c>!</c> or <c>^</c> into negation and escaping metacharacters.</summary>
    private static int AppendCharClass(string s, int i, StringBuilder sb)
    {
        var close = s.IndexOf(']', i + 1);
        if (close < 0)
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

    /// <summary>Emit the escaped character after a backslash as a regex literal (a trailing backslash is literal).</summary>
    private static int AppendEscaped(string s, int i, StringBuilder sb)
    {
        if (i + 1 < s.Length)
        {
            sb.Append(Regex.Escape(s[i + 1].ToString()));
            return i + 2;
        }

        sb.Append(Regex.Escape("\\"));
        return i + 1;
    }

    /// <summary>Build the escaped <c>basePath/</c> regex prefix that anchors a rule under a nested ignore file.</summary>
    private static string BuildPrefix(string basePath)
    {
        if (string.IsNullOrEmpty(basePath))
        {
            return string.Empty;
        }

        var escaped = string.Join('/', basePath.Split('/').Select(Regex.Escape));
        return string.Concat(escaped, "/");
    }
}