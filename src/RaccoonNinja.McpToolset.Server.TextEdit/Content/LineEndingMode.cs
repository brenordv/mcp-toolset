namespace RaccoonNinja.McpToolset.Server.TextEdit.Content;

/// <summary>How <c>normalize_files</c> should treat line terminators.</summary>
public enum LineEndingMode
{
    /// <summary>Leave every physical terminator exactly as it is (mixed endings survive).</summary>
    Preserve = 1,

    /// <summary>Rewrite every terminator to a single LF.</summary>
    Lf = 2,

    /// <summary>Rewrite every terminator to a CRLF pair.</summary>
    Crlf = 3,
}