using System.Security.Cryptography;
using System.Text;

namespace RaccoonNinja.McpToolset.Server.FileVault.Logging;

/// <summary>
/// Scrubbing helpers for the two redaction-rule exceptions: non-domain exception text (capped and
/// control-stripped) and opaque-value hashes for log correlation.
/// </summary>
public static class LogScrubbing
{
    /// <summary>Maximum exception text retained on a log record.</summary>
    private const int ExceptionTextMaxChars = 512;

    /// <summary>Cap exception text to the last <see cref="ExceptionTextMaxChars"/> characters and strip control chars.</summary>
    /// <param name="raw">The rendered exception text.</param>
    /// <returns>The scrubbed tail.</returns>
    public static string ScrubExceptionText(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var capped = raw.Length > ExceptionTextMaxChars ? raw[^ExceptionTextMaxChars..] : raw;
        var builder = new StringBuilder(capped.Length);
        foreach (var ch in capped)
        {
            if (ch == ' ' || !char.IsControl(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Return an 8-hex-char SHA-256 prefix of <paramref name="value"/>. Used when log records need
    /// to correlate two emissions about the same opaque user value (an FTS query, a heading)
    /// without revealing the value itself.
    /// </summary>
    /// <param name="value">The opaque value to hash.</param>
    /// <returns>The 8-character hash prefix.</returns>
    public static string HashedParameter(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        return Convert.ToHexStringLower(bytes)[..8];
    }
}