namespace RaccoonNinja.McpToolset.Files.Text;

/// <summary>The line-ending style detected across a text file.</summary>
public enum LineEndingKind
{
    /// <summary>No line terminators at all (a single line, or empty).</summary>
    None = 1,

    /// <summary>All terminators are LF (<c>\n</c>).</summary>
    Lf = 2,

    /// <summary>All terminators are CRLF (<c>\r\n</c>).</summary>
    Crlf = 3,

    /// <summary>All terminators are lone CR (<c>\r</c>).</summary>
    Cr = 4,

    /// <summary>More than one terminator style is present.</summary>
    Mixed = 5,
}