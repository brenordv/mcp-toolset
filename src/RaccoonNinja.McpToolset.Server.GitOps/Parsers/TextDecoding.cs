using System.Text;

namespace RaccoonNinja.McpToolset.Server.GitOps.Parsers;

/// <summary>UTF-8 decoding helpers shared across parsers.</summary>
public static class TextDecoding
{
    /// <summary>How many leading bytes the binary check scans, matching git's own <c>buffer_is_binary</c>.</summary>
    private const int FirstFewBytes = 8000;

    /// <summary>Decode raw git bytes as UTF-8 (invalid sequences become replacement chars).</summary>
    public static string Decode(byte[] raw)
    {
        return raw is null or { Length: 0 }
            ? string.Empty
            : Encoding.UTF8.GetString(raw);
    }

    /// <summary>Whether <paramref name="raw"/> looks binary by git's heuristic: a NUL byte in the first 8000 bytes.</summary>
    /// <param name="raw">The content bytes.</param>
    /// <returns><c>true</c> when a NUL occurs in the scanned prefix; <c>false</c> for null or empty input.</returns>
    public static bool IsLikelyBinary(byte[] raw)
    {
        if (raw is null or { Length: 0 })
        {
            return false;
        }

        return raw.AsSpan(0, Math.Min(raw.Length, FirstFewBytes)).Contains((byte)0);
    }
}