using System.Security.Cryptography;
using System.Text;

namespace RaccoonNinja.McpToolset.Server.FileVault.Logging;

/// <summary>
/// Scrubbing helper: opaque-value hashes for log correlation.
/// </summary>
public static class LogScrubbing
{
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