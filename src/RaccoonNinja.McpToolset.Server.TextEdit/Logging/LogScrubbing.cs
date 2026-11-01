using System.Security.Cryptography;
using System.Text;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Logging;

/// <summary>
/// Scrubbing helpers for the log fields that carry derived values: an exception's type chain and an
/// opaque-value hash used to correlate records about the same root without ever writing the absolute path.
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