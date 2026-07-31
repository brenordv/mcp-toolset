using System.Text;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Content;

/// <summary>
/// Decodes a file to text and re-encodes it byte-faithfully. The detector's <see cref="Encoding"/> instances
/// are built BOM-less and do not strip a mark, so decoding leaves a leading U+FEFF that this type removes
/// once, and re-encoding prepends the canonical mark back only when the file is meant to keep it. Keeping the
/// mark handling here (rather than in a transform) is what lets a BOM-less UTF-16 file round-trip
/// byte-identical through an edit.
/// </summary>
public static class TextCodec
{
    private const char ByteOrderMark = (char)0xFEFF;

    /// <summary>Decode <paramref name="bytes"/> with <paramref name="encoding"/>, dropping a leading BOM when present.</summary>
    /// <param name="bytes">The raw file bytes.</param>
    /// <param name="encoding">The (BOM-less) encoding to decode with.</param>
    /// <param name="hasBom">Whether the payload began with a byte-order mark.</param>
    /// <returns>The decoded text with no leading U+FEFF.</returns>
    public static string Decode(byte[] bytes, Encoding encoding, bool hasBom)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(encoding);

        var text = encoding.GetString(bytes);
        if (hasBom && text.Length > 0 && text[0] == ByteOrderMark)
        {
            text = text[1..];
        }

        return text;
    }

    /// <summary>Re-encode <paramref name="text"/> with <paramref name="encoding"/>, prepending the mark when <paramref name="withBom"/>.</summary>
    /// <param name="text">The text to encode (must not carry a leading U+FEFF).</param>
    /// <param name="encoding">The (BOM-less) encoding to encode with.</param>
    /// <param name="withBom">Whether to prepend the canonical byte-order mark for <paramref name="encodingName"/>.</param>
    /// <param name="encodingName">The canonical encoding name used to pick the mark bytes.</param>
    /// <returns>The encoded bytes.</returns>
    public static byte[] Encode(string text, Encoding encoding, bool withBom, string encodingName)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(encoding);

        var content = encoding.GetBytes(text);
        if (!withBom)
        {
            return content;
        }

        var mark = MarkFor(encodingName);
        if (mark.Length == 0)
        {
            return content;
        }

        var result = new byte[mark.Length + content.Length];
        Array.Copy(mark, 0, result, 0, mark.Length);
        Array.Copy(content, 0, result, mark.Length, content.Length);
        return result;
    }

    private static byte[] MarkFor(string encodingName)
        => encodingName switch
        {
            "utf-8" => [0xEF, 0xBB, 0xBF],
            "utf-16le" => [0xFF, 0xFE],
            "utf-16be" => [0xFE, 0xFF],
            "utf-32le" => [0xFF, 0xFE, 0x00, 0x00],
            "utf-32be" => [0x00, 0x00, 0xFE, 0xFF],
            _ => [],
        };
}