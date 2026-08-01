using System.Globalization;
using System.Text;
using RaccoonNinja.McpToolset.Server.TextSearch.Errors;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Paging;

/// <summary>
/// Encodes and decodes the opaque pagination cursor, pinned to the request's target. Every cursor
/// carries the normalized <c>root</c> target it was issued for; a resume with a different target (or a
/// malformed token) is refused, so a tampered cursor cannot widen scope past the request's selector and
/// skip the package-search guard. The inner value is only ever an ordinal comparison key (file lists) or
/// a root name plus a skip count (search); it is never used to select a target or opened.
/// </summary>
public static class Cursor
{
    private const int MaxDecodedLength = 8192;

    /// <summary>Encode a file-list cursor: the target plus the composite key to resume after.</summary>
    /// <param name="target">The request's <c>root</c> target.</param>
    /// <param name="key">The composite <c>{rootIndex}\0{path}</c> key of the last returned item.</param>
    /// <returns>The opaque cursor.</returns>
    public static string EncodeList(string target, string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Encode(target, key);
    }

    /// <summary>Decode a file-list cursor, verifying it was issued for <paramref name="target"/>.</summary>
    /// <param name="target">The request's <c>root</c> target.</param>
    /// <param name="cursor">The opaque cursor.</param>
    /// <returns>The composite key to resume after (ordinal comparison only).</returns>
    /// <exception cref="TextSearchException">Thrown (as <c>InvalidArgument</c>) when malformed or for a different target.</exception>
    public static string DecodeList(string target, string cursor)
        => Inner(target, cursor);

    /// <summary>Encode a search cursor: the target, the root of the last match, and the per-root skip count.</summary>
    /// <param name="target">The request's <c>root</c> target.</param>
    /// <param name="root">The name of the root the last emitted match was in.</param>
    /// <param name="offset">The number of matches consumed within that root.</param>
    /// <returns>The opaque cursor.</returns>
    public static string EncodeSearch(string target, string root, int offset)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        return Encode(target, string.Create(CultureInfo.InvariantCulture, $"{root}\n{offset}"));
    }

    /// <summary>Decode a search cursor, verifying it was issued for <paramref name="target"/>.</summary>
    /// <param name="target">The request's <c>root</c> target.</param>
    /// <param name="cursor">The opaque cursor.</param>
    /// <returns>The root to resume in and the per-root skip count.</returns>
    /// <exception cref="TextSearchException">Thrown (as <c>InvalidArgument</c>) when malformed or for a different target.</exception>
    public static (string Root, int Offset) DecodeSearch(string target, string cursor)
    {
        var inner = Inner(target, cursor);
        var newline = inner.IndexOf('\n', StringComparison.Ordinal);
        if (newline < 0
            || !int.TryParse(inner.AsSpan(newline + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var offset))
        {
            throw TextSearchException.InvalidArgument("cursor is malformed");
        }

        return (inner[..newline], offset);
    }

    private static string Encode(string target, string inner)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Concat(Normalize(target), "\n", inner)));

    private static string Inner(string target, string cursor)
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
        var newline = payload.IndexOf('\n', StringComparison.Ordinal);
        if (newline < 0 || !payload.AsSpan(0, newline).SequenceEqual(Normalize(target)))
        {
            throw TextSearchException.InvalidArgument("cursor is for a different query");
        }

        return payload[(newline + 1)..];
    }

    private static string Normalize(string target)
        => string.IsNullOrWhiteSpace(target) ? string.Empty : target.Trim().ToLowerInvariant();
}