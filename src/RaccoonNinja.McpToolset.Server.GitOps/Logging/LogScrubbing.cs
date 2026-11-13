using System.Security.Cryptography;
using System.Text;

namespace RaccoonNinja.McpToolset.Server.GitOps.Logging;

/// <summary>
/// Scrubbing helpers covering the redaction-rule exceptions: git stderr tails and opaque-value
/// hashes for log correlation.
/// </summary>
public static class LogScrubbing
{
    public const int StderrTailMaxBytes = 512;

    /// <summary>Return at most <see cref="StderrTailMaxBytes"/> of raw stderr with control chars stripped.</summary>
    public static string ScrubStderrTail(byte[] raw)
    {
        if (raw == null || raw.Length == 0)
            return string.Empty;

        var capped = raw.Length > StderrTailMaxBytes
            ? raw.AsSpan(raw.Length - StderrTailMaxBytes).ToArray()
            : raw;

        var text = Encoding.UTF8.GetString(capped);
        return StripControls(text);
    }

    /// <summary>
    /// Return an 8-hex-char SHA-256 prefix of <paramref name="value"/>. Use when log
    /// records need to correlate two emissions about the same opaque user value
    /// without revealing the value itself.
    /// </summary>
    public static string HashedParameter(string value)
    {
        value ??= string.Empty;

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));

        return Convert.ToHexStringLower(bytes)[..8];
    }

    private static string StripControls(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text ?? string.Empty;
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