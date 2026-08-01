namespace RaccoonNinja.McpToolset.Files.Text;

/// <summary>Wire-name mapping for <see cref="LineEndingKind"/>.</summary>
public static class LineEndingKindExtensions
{
    /// <summary>Return the lowercase wire name reported in the tool output.</summary>
    /// <param name="kind">The line-ending kind.</param>
    /// <returns>One of <c>none</c>, <c>lf</c>, <c>crlf</c>, <c>cr</c>, <c>mixed</c>.</returns>
    public static string ToWire(this LineEndingKind kind)
        => kind switch
        {
            LineEndingKind.None => "none",
            LineEndingKind.Lf => "lf",
            LineEndingKind.Crlf => "crlf",
            LineEndingKind.Cr => "cr",
            LineEndingKind.Mixed => "mixed",
            _ => "none",
        };
}