using System.Text.RegularExpressions;
using RaccoonNinja.McpToolset.Files.Text;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Content;

/// <summary>
/// Runs a <see cref="LineMatcher"/> over a decoded document, line by line. A regex match timeout
/// stops the file and is reported (never swallowed) so the caller can count it as a refusal. Emitted
/// line text is capped so a minified single line cannot flood model context.
/// </summary>
public static class ContentSearch
{
    /// <summary>The maximum number of characters of a line emitted in a match or context entry.</summary>
    public const int MaxEmittedLineLength = 2000;

    /// <summary>Search <paramref name="document"/> and return its matches with context.</summary>
    /// <param name="document">The decoded document (a binary document yields no matches).</param>
    /// <param name="matcher">The line matcher (regex or literal).</param>
    /// <param name="contextLines">How many lines of context to include before and after each match.</param>
    /// <param name="maxMatchesPerFile">The per-file match cap.</param>
    /// <returns>The file search outcome.</returns>
    public static FileSearchOutcome SearchFile(
        TextDocument document,
        LineMatcher matcher,
        int contextLines,
        int maxMatchesPerFile)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(matcher);

        var matches = new List<ContentMatch>();
        if (document.IsBinary)
        {
            return new FileSearchOutcome(matches, TimedOut: false, CappedPerFile: false);
        }

        var lines = document.Lines;
        var capped = false;
        try
        {
            foreach (var line in lines)
            {
                foreach (var (start, length) in matcher.Matches(line.Content))
                {
                    if (matches.Count >= maxMatchesPerFile)
                    {
                        capped = true;
                        break;
                    }

                    matches.Add(Build(lines, line, start, length, contextLines));
                }

                if (capped)
                {
                    break;
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return new FileSearchOutcome(matches, TimedOut: true, capped);
        }

        return new FileSearchOutcome(matches, TimedOut: false, capped);
    }

    private static ContentMatch Build(
        IReadOnlyList<TextLine> lines,
        TextLine line,
        int start,
        int length,
        int contextLines)
        => new()
        {
            Line = line.Number,
            Column = start + 1,
            Text = Cap(line.Content),
            MatchStart = start,
            MatchEnd = start + length,
            Before = Context(lines, line.Number - contextLines, line.Number - 1, contextLines),
            After = Context(lines, line.Number + 1, line.Number + contextLines, contextLines),
        };

    private static List<ContextLine> Context(IReadOnlyList<TextLine> lines, int from, int to, int contextLines)
    {
        if (contextLines <= 0)
        {
            return null;
        }

        var list = new List<ContextLine>();
        for (var number = Math.Max(1, from); number <= Math.Min(lines.Count, to); number++)
        {
            list.Add(new ContextLine(number, Cap(lines[number - 1].Content)));
        }

        return list.Count == 0 ? null : list;
    }

    private static string Cap(string text)
        => text.Length <= MaxEmittedLineLength ? text : text[..MaxEmittedLineLength];
}