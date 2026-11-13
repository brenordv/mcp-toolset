using System.Security.Cryptography;
using System.Text;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Logging;

/// <summary>
/// Scrubbing helper for the log field that carries a derived value: an opaque-value hash used to
/// correlate records about the same root without ever writing the absolute path.
/// </summary>
public static class LogScrubbing
{
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
}