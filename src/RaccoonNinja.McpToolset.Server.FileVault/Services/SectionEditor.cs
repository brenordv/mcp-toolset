using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using RaccoonNinja.McpToolset.Server.FileVault.Errors;

namespace RaccoonNinja.McpToolset.Server.FileVault.Services;

/// <summary>
/// Markdown section splicing for <c>vault_edit_section</c>. Replaces the body under a heading
/// (up to the next same-or-higher heading) while preserving every character outside that range
/// verbatim. Offsets are UTF-16 char positions (Markdig spans), applied consistently in string
/// space.
/// </summary>
public static class SectionEditor
{
    /// <summary>
    /// Replace the body under <paramref name="heading"/> with <paramref name="newBody"/>.
    /// </summary>
    /// <param name="source">The full markdown document.</param>
    /// <param name="heading">The heading text identifying the target section (leading <c>#</c> tolerated).</param>
    /// <param name="newBody">The replacement body.</param>
    /// <returns>The spliced document.</returns>
    /// <exception cref="VaultException">
    /// Thrown with <see cref="VaultErrorCode.HeadingNotFound"/> or
    /// <see cref="VaultErrorCode.AmbiguousHeading"/> for heading lookup failures.
    /// </exception>
    public static string SpliceSection(string source, string heading, string newBody)
    {
        ArgumentNullException.ThrowIfNull(source);
        var headings = CollectHeadings(source);
        var target = NormalizeHeading(heading);

        var matches = headings
            .Select((info, index) => (info, index))
            .Where(pair => string.Equals(NormalizeHeading(pair.info.Text), target, StringComparison.Ordinal))
            .Select(pair => pair.index)
            .ToList();

        var targetIndex = matches.Count switch
        {
            0 => throw VaultException.HeadingNotFound(heading),
            1 => matches[0],
            _ => throw VaultException.AmbiguousHeading(heading),
        };

        var targetHeading = headings[targetIndex];

        // Body starts on the line after the heading line.
        var newlineAt = source.IndexOf('\n', targetHeading.Start);
        var bodyStart = newlineAt >= 0 ? newlineAt + 1 : source.Length;

        // Section ends at the next heading of the same or higher level.
        var sectionEnd = headings
            .Skip(targetIndex + 1)
            .Where(info => info.Level <= targetHeading.Level)
            .Select(info => (int?)info.Start)
            .FirstOrDefault() ?? source.Length;
        bodyStart = Math.Min(bodyStart, sectionEnd);

        var replacement = (newBody ?? string.Empty).TrimEnd('\n') + "\n";
        if (sectionEnd < source.Length)
        {
            // Keep a blank line before the following heading.
            replacement += "\n";
        }

        return string.Concat(source.AsSpan(0, bodyStart), replacement, source.AsSpan(sectionEnd));
    }

    private static List<(byte Level, int Start, string Text)> CollectHeadings(string source)
    {
        var document = Markdown.Parse(source);
        var headings = new List<(byte Level, int Start, string Text)>();
        foreach (var block in document.Descendants<HeadingBlock>())
        {
            headings.Add(((byte)block.Level, block.Span.Start, InlineText(block)));
        }

        headings.Sort((a, b) => a.Start.CompareTo(b.Start));
        return headings;
    }

    /// <summary>Concatenate the heading's literal and code-span text, matching pulldown-cmark's Text/Code events.</summary>
    private static string InlineText(HeadingBlock block)
    {
        if (block.Inline is null)
        {
            return string.Empty;
        }

        var text = new System.Text.StringBuilder();
        foreach (var inline in block.Inline.Descendants())
        {
            switch (inline)
            {
                case LiteralInline literal:
                    text.Append(literal.Content.ToString());
                    break;
                case CodeInline code:
                    text.Append(code.Content);
                    break;
                default:
                    break;
            }
        }

        return text.ToString();
    }

    /// <summary>Normalize a heading for comparison: trim, drop a leading run of <c>#</c>, trim again.</summary>
    private static string NormalizeHeading(string value)
        => (value ?? string.Empty).Trim().TrimStart('#').Trim();
}