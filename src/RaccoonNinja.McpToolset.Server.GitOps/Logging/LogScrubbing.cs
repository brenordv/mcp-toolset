using System.Security.Cryptography;
using System.Text;

namespace RaccoonNinja.McpToolset.Server.GitOps.Logging;

/// <summary>
/// Scrubbing helpers covering the redaction-rule exceptions: git stderr tails, an exception's
/// type chain, and opaque-value hashes for log correlation.
/// </summary>
public static class LogScrubbing
{
    public const int StderrTailMaxBytes = 512;

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