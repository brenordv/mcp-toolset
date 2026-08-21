using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using RaccoonNinja.McpToolset.Server.FileVault.Errors;

namespace RaccoonNinja.McpToolset.Server.FileVault.Services;

/// <summary>
/// Markdown section splicing for <c>vault_edit_section</c>. Replaces the body under a heading
/// (up to the next same-or-higher heading) while preserving every character outside that range
/// verbatim. A heading is matched by either its rendered inline text (markdown delimiters stripped)
/// or its verbatim source text (delimiters kept), so a caller can copy the heading straight from the
/// document. Offsets are UTF-16 char positions (Markdig spans), applied consistently in string space.
/// </summary>
public static class SectionEditor
{
    /// <summary>
    /// Replace the body under <paramref name="heading"/> with <paramref name="newBody"/>.
    /// </summary>
    /// <param name="source">The full markdown document.</param>
    /// <param name="heading">
    /// The heading identifying the target section, given as either rendered plain text or verbatim
    /// source with inline markdown kept (code spans, emphasis, links). A leading <c>#</c> run is tolerated.
    /// </param>
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
            .Where(pair => string.Equals(pair.info.Rendered, target, StringComparison.Ordinal)
                || string.Equals(pair.info.Verbatim, target, StringComparison.Ordinal))
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

    private static List<HeadingInfo> CollectHeadings(string source)
    {
        var document = Markdown.Parse(source);
        var headings = new List<HeadingInfo>();
        foreach (var block in document.Descendants<HeadingBlock>())
        {
            var rendered = NormalizeHeading(InlineText(block));
            var verbatim = NormalizeHeading(VerbatimText(source, block));
            headings.Add(new HeadingInfo((byte)block.Level, block.Span.Start, rendered, verbatim));
        }

        headings.Sort((a, b) => a.Start.CompareTo(b.Start));
        return headings;
    }

    /// <summary>
    /// The heading's rendered inline text: literal text plus code-span content, with the markdown
    /// delimiters (backticks, emphasis markers, link syntax) dropped. One of the two forms a caller
    /// may match against.
    /// </summary>
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

    /// <summary>
    /// The heading's verbatim source text, with inline-markdown delimiters (backticks, emphasis
    /// markers, link syntax) intact, so a caller who copies the heading straight from the document can
    /// target it. Taken from the block's source span; a trailing setext underline line, when present,
    /// is dropped. One of the two forms a caller may match against.
    /// </summary>
    private static string VerbatimText(string source, HeadingBlock block)
    {
        var start = block.Span.Start;
        if (start < 0 || start >= source.Length)
        {
            return string.Empty;
        }

        var length = Math.Min(block.Span.Length, source.Length - start);
        if (length <= 0)
        {
            return string.Empty;
        }

        var raw = source.Substring(start, length);
        var underlineAt = raw.LastIndexOf('\n');
        return underlineAt >= 0 ? raw[..underlineAt] : raw;
    }

    /// <summary>Normalize a heading for comparison: trim, drop a leading run of <c>#</c>, trim again.</summary>
    private static string NormalizeHeading(string value)
        => (value ?? string.Empty).Trim().TrimStart('#').Trim();

    /// <summary>
    /// A collected heading: its level, source offset, and the two normalized forms a target is matched
    /// against. <see cref="Rendered"/> is the inline text with markdown delimiters stripped;
    /// <see cref="Verbatim"/> is the source text with delimiters kept.
    /// </summary>
    private readonly record struct HeadingInfo(byte Level, int Start, string Rendered, string Verbatim);
}