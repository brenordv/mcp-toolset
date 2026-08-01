using System.Globalization;
using System.Text;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Content;

/// <summary>
/// Renders a line-oriented unified diff between two versions of a file, for a <c>dry_run</c> preview. Lines
/// are compared by an LCS so the diff is minimal, and changes are grouped into hunks with a few lines of
/// context. The line count is bounded: a file past the cap yields a short note instead of a diff, so a
/// pathological input cannot make the preview quadratically expensive.
/// </summary>
public static class UnifiedDiff
{
    private const int MaxDiffLines = 5_000;
    private const int Context = 3;

    /// <summary>Format the unified diff from <paramref name="oldText"/> to <paramref name="newText"/> for <paramref name="path"/>.</summary>
    /// <param name="oldText">The text before the edit.</param>
    /// <param name="newText">The text after the edit.</param>
    /// <param name="path">The root-relative path used in the diff header.</param>
    /// <returns>The unified diff, or a short note when either version exceeds the line cap.</returns>
    public static string Format(string oldText, string newText, string path)
    {
        ArgumentNullException.ThrowIfNull(oldText);
        ArgumentNullException.ThrowIfNull(newText);

        var oldLines = SplitLines(oldText);
        var newLines = SplitLines(newText);

        var header = string.Create(CultureInfo.InvariantCulture, $"--- a/{path}\n+++ b/{path}\n");
        if (oldLines.Count > MaxDiffLines || newLines.Count > MaxDiffLines)
        {
            return header + string.Create(CultureInfo.InvariantCulture, $"(diff omitted: file exceeds {MaxDiffLines} lines)\n");
        }

        var ops = Diff(oldLines, newLines);
        return header + RenderHunks(ops, oldLines, newLines);
    }

    private static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        var i = 0;
        while (i < text.Length)
        {
            var start = i;
            while (i < text.Length && text[i] != '\n')
            {
                i++;
            }

            var line = text[start..i];
            if (line.EndsWith('\r'))
            {
                line = line[..^1];
            }

            lines.Add(line);
            if (i < text.Length)
            {
                i++;
            }
        }

        return lines;
    }

    private static List<Op> Diff(List<string> a, List<string> b)
    {
        var n = a.Count;
        var m = b.Count;
        var dp = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                dp[i, j] = string.Equals(a[i], b[j], StringComparison.Ordinal)
                    ? dp[i + 1, j + 1] + 1
                    : Math.Max(dp[i + 1, j], dp[i, j + 1]);
            }
        }

        var ops = new List<Op>();
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (string.Equals(a[x], b[y], StringComparison.Ordinal))
            {
                ops.Add(new Op(OpKind.Equal, x, y));
                x++;
                y++;
            }
            else if (dp[x + 1, y] >= dp[x, y + 1])
            {
                ops.Add(new Op(OpKind.Delete, x, y));
                x++;
            }
            else
            {
                ops.Add(new Op(OpKind.Insert, x, y));
                y++;
            }
        }

        while (x < n)
        {
            ops.Add(new Op(OpKind.Delete, x++, y));
        }

        while (y < m)
        {
            ops.Add(new Op(OpKind.Insert, x, y++));
        }

        return ops;
    }

    private static string RenderHunks(List<Op> ops, List<string> a, List<string> b)
    {
        var include = MarkContext(ops);
        var builder = new StringBuilder();
        var i = 0;
        while (i < ops.Count)
        {
            if (!include[i])
            {
                i++;
                continue;
            }

            var start = i;
            while (i < ops.Count && include[i])
            {
                i++;
            }

            AppendHunk(builder, ops, a, b, start, i);
        }

        return builder.ToString();
    }

    private static bool[] MarkContext(List<Op> ops)
    {
        var include = new bool[ops.Count];
        for (var i = 0; i < ops.Count; i++)
        {
            if (ops[i].Kind == OpKind.Equal)
            {
                continue;
            }

            var from = Math.Max(0, i - Context);
            var to = Math.Min(ops.Count - 1, i + Context);
            for (var k = from; k <= to; k++)
            {
                include[k] = true;
            }
        }

        return include;
    }

    private static void AppendHunk(StringBuilder builder, List<Op> ops, List<string> a, List<string> b, int start, int end)
    {
        var oldCount = 0;
        var newCount = 0;
        for (var k = start; k < end; k++)
        {
            if (ops[k].Kind != OpKind.Insert)
            {
                oldCount++;
            }

            if (ops[k].Kind != OpKind.Delete)
            {
                newCount++;
            }
        }

        var oldStart = oldCount == 0 ? ops[start].OldIndex : ops[start].OldIndex + 1;
        var newStart = newCount == 0 ? ops[start].NewIndex : ops[start].NewIndex + 1;
        builder.Append(string.Create(CultureInfo.InvariantCulture, $"@@ -{oldStart},{oldCount} +{newStart},{newCount} @@\n"));

        for (var k = start; k < end; k++)
        {
            var op = ops[k];
            switch (op.Kind)
            {
                case OpKind.Equal:
                    builder.Append(' ').Append(a[op.OldIndex]).Append('\n');
                    break;
                case OpKind.Delete:
                    builder.Append('-').Append(a[op.OldIndex]).Append('\n');
                    break;
                case OpKind.Insert:
                    builder.Append('+').Append(b[op.NewIndex]).Append('\n');
                    break;
                default:
                    break;
            }
        }
    }

    private enum OpKind
    {
        Equal = 1,
        Delete = 2,
        Insert = 3,
    }

    private readonly record struct Op(OpKind Kind, int OldIndex, int NewIndex);
}