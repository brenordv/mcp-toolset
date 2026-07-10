using System.Globalization;
using System.Text;

namespace RaccoonNinja.McpToolset.Server.FileVault.Domain;

/// <summary>
/// A small, dependency-free line diff used to build the conflict diff hint. It is intentionally
/// simple (LCS over whole lines, no fuzz, no hunk headers). Its job is to let the assistant see
/// what changed between the version it read and the current one, not to be a production diff tool.
/// </summary>
public static class LineDiff
{
    /// <summary>
    /// Produce a unified-style, line-oriented diff from <paramref name="baseText"/> to
    /// <paramref name="currentText"/>. Lines are prefixed <c>-</c> (only in base), <c>+</c>
    /// (only in current), or a single space (common). Output is bounded to
    /// <paramref name="maxLines"/>; truncation is marked with the Rust-parity trailer.
    /// </summary>
    /// <param name="baseText">The content the caller derived its write from.</param>
    /// <param name="currentText">The content currently stored.</param>
    /// <param name="maxLines">The maximum number of diff lines before truncation.</param>
    /// <returns>The rendered diff.</returns>
    /// <remarks>
    /// The LCS table is O(baseLines * currentLines) in memory; callers must gate invocation by
    /// line-count product; this method does not guard itself, matching the Rust
    /// helper's contract.
    /// </remarks>
    public static string Compute(string baseText, string currentText, int maxLines)
    {
        var a = SplitLines(baseText);
        var b = SplitLines(currentText);

        // Classic LCS length table over lines.
        var n = a.Length;
        var m = b.Length;
        var lcs = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                lcs[i, j] = string.Equals(a[i], b[j], StringComparison.Ordinal)
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        // Walk the table to emit the diff.
        var output = new List<string>();
        var (x, y) = (0, 0);
        while (x < n && y < m)
        {
            if (string.Equals(a[x], b[y], StringComparison.Ordinal))
            {
                output.Add(" " + a[x]);
                x++;
                y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                output.Add("-" + a[x]);
                x++;
            }
            else
            {
                output.Add("+" + b[y]);
                y++;
            }
        }

        while (x < n)
        {
            output.Add("-" + a[x]);
            x++;
        }

        while (y < m)
        {
            output.Add("+" + b[y]);
            y++;
        }

        if (output.Count > maxLines)
        {
            var omitted = output.Count - maxLines;
            output.RemoveRange(maxLines, omitted);
            output.Add(string.Create(CultureInfo.InvariantCulture, $"… ({omitted} more diff lines omitted)"));
        }

        return string.Join('\n', output);
    }

    /// <summary>Count the lines of <paramref name="text"/> the same way <see cref="Compute"/> will.</summary>
    /// <param name="text">The content to measure.</param>
    /// <returns>The line count.</returns>
    public static long CountLines(string text)
        => SplitLines(text).LongLength;

    /// <summary>
    /// Split like Rust's <c>str::lines</c>: separators are <c>\n</c> (a preceding <c>\r</c> is
    /// stripped), and a trailing newline does not produce an empty final line.
    /// </summary>
    private static string[] SplitLines(string text)
    {
        // IsNullOrEmpty, not IsNullOrWhiteSpace: Rust's str::lines yields lines for
        // whitespace-only input ("\n" is one empty line), and this must match it.
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var lines = new List<string>();
        var builder = new StringBuilder();
        foreach (var ch in text)
        {
            if (ch == '\n')
            {
                if (builder.Length > 0 && builder[^1] == '\r')
                {
                    builder.Length--;
                }

                lines.Add(builder.ToString());
                builder.Clear();
            }
            else
            {
                builder.Append(ch);
            }
        }

        if (builder.Length > 0)
        {
            lines.Add(builder.ToString());
        }

        return [.. lines];
    }
}