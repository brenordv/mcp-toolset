using System.Security.Cryptography;
using System.Text;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Logging;

/// <summary>
/// Scrubbing helpers for the log fields that carry potentially sensitive bytes: an exception tail
/// (control-stripped and capped) and an opaque-value hash used to correlate records about the same
/// root without ever writing the absolute path.
/// </summary>
public static class LogScrubbing
{
    /// <summary>The maximum number of trailing bytes of an exception rendering that reach the log.</summary>
    public const int ExceptionTailMaxBytes = 512;

    /// <summary>Return at most <see cref="ExceptionTailMaxBytes"/> of raw text with control chars stripped.</summary>
    /// <param name="raw">The raw bytes (for example an exception rendering) to cap and clean.</param>
    /// <returns>The capped, control-stripped tail, or the empty string when there is nothing to emit.</returns>
    public static string ScrubTail(byte[] raw)
    {
        if (raw == null || raw.Length == 0)
        {
            return string.Empty;
        }

        var capped = raw.Length > ExceptionTailMaxBytes
            ? raw.AsSpan(raw.Length - ExceptionTailMaxBytes).ToArray()
            : raw;

        return StripControls(Encoding.UTF8.GetString(capped));
    }

    /// <summary>
    /// Return an 8-hex-char SHA-256 prefix of <paramref name="value"/>. Used to correlate log records
    /// about the same absolute root without revealing the root itself, per the machine-privacy rule.
    /// </summary>
    /// <param name="value">The value to hash; <c>null</c> is treated as empty.</param>
    /// <returns>The lowercase 8-character hash prefix.</returns>
    public static string HashedValue(string value)
    {
        value ??= string.Empty;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(bytes)[..8];
    }

    private static string StripControls(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text ?? string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch == ' ' || !char.IsControl(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }
}