using RaccoonNinja.McpToolset.Files.Text;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Content;

/// <summary>
/// A decoded text file: the detected encoding, the lines (terminator-free, with 1-based numbers), and
/// the whole-file statistics that inspection reports. All of it comes from a single pass so the line
/// model that search and read use and the statistics that inspect reports can never disagree.
/// </summary>
public sealed class TextDocument
{
    private TextDocument(
        DetectedEncoding encoding,
        bool isBinary,
        IReadOnlyList<TextLine> lines,
        LineEndingKind lineEndings,
        bool finalNewline,
        int trailingWhitespaceLines)
    {
        Encoding = encoding;
        IsBinary = isBinary;
        Lines = lines;
        LineEndings = lineEndings;
        FinalNewline = finalNewline;
        TrailingWhitespaceLines = trailingWhitespaceLines;
    }

    /// <summary>The detected encoding (carries the name, confidence, and BOM flag).</summary>
    public DetectedEncoding Encoding { get; }

    /// <summary>Whether the payload was classified as binary; when true, <see cref="Lines"/> is empty.</summary>
    public bool IsBinary { get; }

    /// <summary>The decoded lines, terminator-free, in file order.</summary>
    public IReadOnlyList<TextLine> Lines { get; }

    /// <summary>The line-ending style across the file.</summary>
    public LineEndingKind LineEndings { get; }

    /// <summary>Whether the file ends with a line terminator.</summary>
    public bool FinalNewline { get; }

    /// <summary>How many lines carry trailing whitespace (a space or tab before the terminator).</summary>
    public int TrailingWhitespaceLines { get; }

    /// <summary>The number of lines.</summary>
    public int LineCount => Lines.Count;

    /// <summary>
    /// Detect the encoding of <paramref name="bytes"/> and decode it into a line model. A binary
    /// payload yields a document with <see cref="IsBinary"/> set and no lines; it is never decoded.
    /// </summary>
    /// <param name="bytes">The raw file bytes.</param>
    /// <param name="detector">The encoding detector.</param>
    /// <returns>The decoded document.</returns>
    public static TextDocument Load(byte[] bytes, IEncodingDetector detector)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(detector);

        var detected = detector.Detect(bytes);
        if (detected.IsBinary || detected.Encoding is null)
        {
            return new TextDocument(detected, isBinary: true, [], LineEndingKind.None, finalNewline: false, 0);
        }

        var text = detected.Encoding.GetString(bytes);

        // The detector's Encoding instances are built BOM-less and do not strip the mark, so a
        // decoded BOM survives as a leading U+FEFF. Drop it once, uniformly across UTF widths, or
        // every line-1 column and match offset would be shifted by one.
        if (detected.HasBom && text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text[1..];
        }

        var (lines, lineEndings, trailingWhitespace) = SplitLines(text);
        var finalNewline = text.Length > 0 && (text[^1] == '\n' || text[^1] == '\r');
        return new TextDocument(detected, isBinary: false, lines, lineEndings, finalNewline, trailingWhitespace);
    }

    /// <summary>
    /// Split <paramref name="text"/> into terminator-free lines with a manual scan that treats
    /// <c>\r\n</c> as one terminator and lone <c>\r</c>/<c>\n</c> as terminators. This pins the line
    /// set to the CR/LF family that editors number by, avoiding both the trailing-<c>\r</c> and lost
    /// offsets of <c>String.Split</c> and the over-splitting of <c>EnumerateLines</c> (which also
    /// breaks on U+0085/U+2028/U+2029/VT/FF).
    /// </summary>
    private static (IReadOnlyList<TextLine> Lines, LineEndingKind Endings, int TrailingWhitespace) SplitLines(string text)
    {
        var lines = new List<TextLine>();
        var n = text.Length;
        var lineStart = 0;
        var lineNo = 1;
        var trailingWhitespace = 0;
        var sawLf = false;
        var sawCrlf = false;
        var sawCr = false;

        var i = 0;
        while (i < n)
        {
            var c = text[i];
            if (c is not ('\r' or '\n'))
            {
                i++;
                continue;
            }

            AddLine(lines, ref lineNo, text, lineStart, i, ref trailingWhitespace);

            if (c == '\r' && i + 1 < n && text[i + 1] == '\n')
            {
                sawCrlf = true;
                i += 2;
            }
            else if (c == '\r')
            {
                sawCr = true;
                i += 1;
            }
            else
            {
                sawLf = true;
                i += 1;
            }

            lineStart = i;
        }

        if (lineStart < n)
        {
            AddLine(lines, ref lineNo, text, lineStart, n, ref trailingWhitespace);
        }

        return (lines, ClassifyEndings(sawLf, sawCrlf, sawCr), trailingWhitespace);
    }

    private static void AddLine(
        List<TextLine> lines,
        ref int lineNo,
        string text,
        int start,
        int end,
        ref int trailingWhitespace)
    {
        var content = text[start..end];
        lines.Add(new TextLine(lineNo, content));
        lineNo++;
        if (content.Length > 0 && (content[^1] == ' ' || content[^1] == '\t'))
        {
            trailingWhitespace++;
        }
    }

    private static LineEndingKind ClassifyEndings(bool sawLf, bool sawCrlf, bool sawCr)
    {
        var kinds = (sawLf ? 1 : 0) + (sawCrlf ? 1 : 0) + (sawCr ? 1 : 0);
        return kinds switch
        {
            0 => LineEndingKind.None,
            > 1 => LineEndingKind.Mixed,
            _ => sawCrlf ? LineEndingKind.Crlf : sawLf ? LineEndingKind.Lf : LineEndingKind.Cr
        };
    }
}