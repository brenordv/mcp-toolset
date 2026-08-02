using System.Globalization;
using System.Text;
using RaccoonNinja.McpToolset.Server.TextSearch.Errors;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Paging;

/// <summary>
/// Encodes and decodes the opaque pagination cursor, pinned to the call's scope. Every cursor carries the
/// base-relative scope key it was issued for; a resume in a different scope, or a malformed token, is
/// refused, so a tampered cursor cannot widen scope past the request's selector. The inner value is only
/// ever an ordinal comparison key (file lists) or a skip count (search); it is never used to select a
/// scope and never opened. The scope key and the inner value are joined by a NUL, which a filesystem path
/// can never contain, so the split stays unambiguous even for a path holding a newline. The scope-key
/// identity uses the host filesystem's case rules, so a case-only difference is treated the same way the
/// filesystem treats it.
/// </summary>
public static class Cursor
{
    private const int MaxDecodedLength = 8192;
    private const char Delimiter = '\0';

    private static readonly StringComparison ScopeComparison =
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>Encode a file-list cursor: the scope key plus the path to resume after.</summary>
    /// <param name="scopeKey">The base-relative scope key of the call.</param>
    /// <param name="key">The scope-relative path of the last returned item.</param>
    /// <returns>The opaque cursor.</returns>
    public static string EncodeList(string scopeKey, string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Encode(scopeKey, key);
    }

    /// <summary>Decode a file-list cursor, verifying it was issued for <paramref name="scopeKey"/>.</summary>
    /// <param name="scopeKey">The base-relative scope key of the call.</param>
    /// <param name="cursor">The opaque cursor.</param>
    /// <returns>The path to resume after (ordinal comparison only).</returns>
    /// <exception cref="TextSearchException">Thrown (as <c>InvalidArgument</c>) when malformed or for a different scope.</exception>
    public static string DecodeList(string scopeKey, string cursor)
        => Inner(scopeKey, cursor);

    /// <summary>Encode a search cursor: the scope key and the number of matches consumed so far.</summary>
    /// <param name="scopeKey">The base-relative scope key of the call.</param>
    /// <param name="offset">The number of matches consumed within the scope.</param>
    /// <returns>The opaque cursor.</returns>
    public static string EncodeSearch(string scopeKey, int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        return Encode(scopeKey, offset.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Decode a search cursor, verifying it was issued for <paramref name="scopeKey"/>.</summary>
    /// <param name="scopeKey">The base-relative scope key of the call.</param>
    /// <param name="cursor">The opaque cursor.</param>
    /// <returns>The number of matches to skip within the scope.</returns>
    /// <exception cref="TextSearchException">Thrown (as <c>InvalidArgument</c>) when malformed or for a different scope.</exception>
    public static int DecodeSearch(string scopeKey, string cursor)
    {
        var inner = Inner(scopeKey, cursor);
        return int.TryParse(inner, NumberStyles.None, CultureInfo.InvariantCulture, out var offset)
            ? offset
            : throw TextSearchException.InvalidArgument("cursor is malformed");
    }

    private static string Encode(string scopeKey, string inner)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(Normalize(scopeKey) + Delimiter + inner));

    private static string Inner(string scopeKey, string cursor)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            throw TextSearchException.InvalidArgument("cursor is malformed");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(cursor);
        }
        catch (FormatException)
        {
            throw TextSearchException.InvalidArgument("cursor is malformed");
        }

        if (bytes.Length > MaxDecodedLength)
        {
            throw TextSearchException.InvalidArgument("cursor is malformed");
        }

        var payload = Encoding.UTF8.GetString(bytes);
        var delimiter = payload.IndexOf(Delimiter);
        if (delimiter < 0 || !payload.AsSpan(0, delimiter).Equals(Normalize(scopeKey), ScopeComparison))
        {
            throw TextSearchException.InvalidArgument("cursor is for a different query");
        }

        return payload[(delimiter + 1)..];
    }

    private static string Normalize(string scopeKey)
        => string.IsNullOrWhiteSpace(scopeKey) ? string.Empty : scopeKey.Trim();
}