using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;

namespace RaccoonNinja.McpToolset.Server.GitOps.Parsers;

/// <summary>
/// Slices a text blob by an inclusive 1-based line range, a byte offset, and a byte cap, on raw bytes,
/// before decoding. Lines end at '\n'; a final line without one still counts. Neither cut splits a UTF-8
/// sequence: a nonzero offset moves forward over continuation bytes (10xxxxxx) and the cap backs off over
/// them, at most three bytes either way. The one exception is a cap smaller than the first character
/// after the offset: the cap keeps at least one byte, so that character is split and a caller paging
/// with the cap still advances.
/// </summary>
public static class BlobSlicer
{
    /// <summary>The longest run of continuation bytes in one UTF-8 sequence, so a boundary search never walks further.</summary>
    private const int MaxContinuationBytes = 3;

    /// <summary>Reject a malformed range, offset, or cap. The checks that need the content (a range starting past the last line, an offset past the end) live in <see cref="Slice"/>.</summary>
    /// <param name="lineStart">First line, 1-based, or null for no range.</param>
    /// <param name="lineEnd">Last line, 1-based inclusive, or null for no range.</param>
    /// <param name="byteOffset">Bytes to skip from the start of the line slice, or null for none.</param>
    /// <param name="maxBytes">Byte cap on the returned content, or null for none.</param>
    /// <exception cref="RejectedArgumentException">The range is half-specified, non-positive, or reversed; the offset is negative; or the cap is below 1.</exception>
    public static void ValidateArguments(int? lineStart, int? lineEnd, int? byteOffset, int? maxBytes)
    {
        if (maxBytes is < 1)
        {
            throw Rejected("maxBytes must be at least 1", "max_bytes");
        }

        if (byteOffset is < 0)
        {
            throw Rejected("byteOffset must be at least 0", "byte_offset");
        }

        if (!lineStart.HasValue && !lineEnd.HasValue)
        {
            return;
        }

        var start = lineStart ?? 0;
        var end = lineEnd ?? 0;
        if (start <= 0 || end < start)
        {
            throw Rejected("line_range must be a positive (start <= end) tuple", "line_range");
        }
    }

    /// <summary>Slice <paramref name="content"/> to the requested lines, offset, and cap.</summary>
    /// <param name="content">The whole blob.</param>
    /// <param name="lineStart">First line, 1-based, or null for no range.</param>
    /// <param name="lineEnd">Last line, 1-based inclusive, or null for no range.</param>
    /// <param name="byteOffset">Bytes to skip from the start of the line slice, or null for none.</param>
    /// <param name="maxBytes">Byte cap on the returned content, or null for none.</param>
    /// <returns>The slice.</returns>
    /// <exception cref="RejectedArgumentException">Any <see cref="ValidateArguments"/> rejection, a range that starts past the last line, or an offset at or past the end of the content.</exception>
    public static BlobSlice Slice(byte[] content, int? lineStart, int? lineEnd, int? byteOffset, int? maxBytes)
    {
        content ??= [];
        ValidateArguments(lineStart, lineEnd, byteOffset, maxBytes);

        var totalLines = CountLines(content);
        var (windowStart, windowEnd) = lineStart.HasValue
            ? FindWindow(content, lineStart.Value, lineEnd.Value, totalLines)
            : (0, content.Length);
        var skip = ApplyOffset(content, windowStart, windowEnd - windowStart, byteOffset ?? 0);
        var start = windowStart + skip;
        var length = windowEnd - start;
        var kept = ApplyCap(content, start, length, maxBytes);
        var truncated = kept < length;
        int? nextByteOffset = truncated ? skip + kept : null;
        return new BlobSlice(content[start..(start + kept)], totalLines, truncated, nextByteOffset);
    }

    /// <summary>The number of '\n' bytes, plus one for a final line that has none.</summary>
    private static int CountLines(byte[] content)
    {
        var newlines = content.AsSpan().Count((byte)'\n');
        return content.Length > 0 && content[^1] != (byte)'\n' ? newlines + 1 : newlines;
    }

    /// <summary>The byte window, end exclusive, holding lines <paramref name="start"/> through <paramref name="end"/>; an end past the last line runs to the end of the content, and a start past the last line is rejected with the line count.</summary>
    private static (int Start, int End) FindWindow(byte[] content, int start, int end, int totalLines)
    {
        if (start > totalLines)
        {
            throw new RejectedArgumentException(
                "line_range starts past the last line",
                new Dictionary<string, object> { ["param"] = "line_range", ["total_lines"] = totalLines });
        }

        var windowStart = 0;
        var line = 1;
        for (var index = 0; index < content.Length; index++)
        {
            if (content[index] != (byte)'\n')
            {
                continue;
            }

            if (line == end)
            {
                return (windowStart, index + 1);
            }

            line++;
            if (line == start)
            {
                windowStart = index + 1;
            }
        }

        return (windowStart, content.Length);
    }

    /// <summary>How many bytes to skip into the <paramref name="length"/>-byte window at <paramref name="start"/>: <paramref name="byteOffset"/>, moved forward to the next character when it lands on a continuation byte; an offset at or past the end is rejected with the window length.</summary>
    private static int ApplyOffset(byte[] content, int start, int length, int byteOffset)
    {
        if (byteOffset == 0)
        {
            return 0;
        }

        if (byteOffset >= length)
        {
            throw new RejectedArgumentException(
                "byteOffset is at or past the end of the content",
                new Dictionary<string, object> { ["param"] = "byte_offset", ["content_bytes"] = length });
        }

        var skip = byteOffset;
        for (var step = 0; step < MaxContinuationBytes && skip < length && IsContinuationByte(content[start + skip]); step++)
        {
            skip++;
        }

        return skip;
    }

    /// <summary>How many of the <paramref name="length"/> bytes at <paramref name="start"/> to keep: all of them under the cap, otherwise the cap backed off to the start of the character it lands in, never below one byte.</summary>
    private static int ApplyCap(byte[] content, int start, int length, int? maxBytes)
    {
        if (maxBytes is not int cap || length <= cap)
        {
            return length;
        }

        var kept = cap;
        for (var step = 0; step < MaxContinuationBytes && kept > 1 && IsContinuationByte(content[start + kept]); step++)
        {
            kept--;
        }

        return kept;
    }

    /// <summary>Whether <paramref name="value"/> is a UTF-8 continuation byte: every byte after the first in a multi-byte sequence has the shape 10xxxxxx.</summary>
    private static bool IsContinuationByte(byte value) => (value & 0b1100_0000) == 0b1000_0000;

    private static RejectedArgumentException Rejected(string message, string param)
        => new(message, new Dictionary<string, object> { ["param"] = param });
}