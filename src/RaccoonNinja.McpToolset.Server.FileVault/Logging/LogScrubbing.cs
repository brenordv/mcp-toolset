using System.Security.Cryptography;
using System.Text;

namespace RaccoonNinja.McpToolset.Server.FileVault.Logging;

/// <summary>
/// Scrubbing helpers for the two redaction-rule exceptions: an exception's type chain and
/// opaque-value hashes for log correlation.
/// </summary>
public static class LogScrubbing
{
    /// <summary>The most exception types <see cref="ExceptionTypeChain"/> lists before it stops.</summary>
    public const int ExceptionChainMaxDepth = 8;

    /// <summary>
    /// Render an exception as its type chain, outer to inner, for the log. A type's name is chosen by the code
    /// that defines the type, never by request data, so unlike a message or a stack trace it cannot carry an
    /// absolute path, a secret, or user content.
    /// </summary>
    /// <param name="exception">The logged exception.</param>
    /// <returns>
    /// The full type names joined by <c> -> </c>, with <c> -> ...</c> appended past
    /// <see cref="ExceptionChainMaxDepth"/> entries.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <c>null</c>.</exception>
    public static string ExceptionTypeChain(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var names = new List<string>();
        var current = exception;
        while (current is not null && names.Count < ExceptionChainMaxDepth)
        {
            names.Add(current.GetType().FullName);
            current = current.InnerException;
        }

        var chain = string.Join(" -> ", names);
        return current is null ? chain : chain + " -> ...";
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