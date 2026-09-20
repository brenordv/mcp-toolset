using System.Buffers.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RaccoonNinja.McpToolset.Server.FileVault.Configuration;
using RaccoonNinja.McpToolset.Server.FileVault.Errors;
using RaccoonNinja.McpToolset.Server.FileVault.Storage;

namespace RaccoonNinja.McpToolset.Server.FileVault.Services;

/// <summary>
/// The opaque keyset cursor for <c>vault_list</c>. It encodes the last row's sort key
/// (<c>updated_at</c>, project, name) as base64url JSON. Decoding is deliberately defensive: a
/// tampered, oversized, or stale cursor becomes a clean <c>invalid_argument</c>, never a raw parser
/// exception whose message could echo the cursor into a full internal-error log.
/// </summary>
public static class ListCursor
{
    /// <summary>The fixed client-facing message for every malformed or stale cursor.</summary>
    public const string Message = "cursor is malformed or stale; re-issue the query without a cursor";

    private const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions DecodeOptions = new()
    {
        MaxDepth = 4,
    };

    /// <summary>Encode the sort key of the last row on a page into an opaque cursor string.</summary>
    /// <param name="row">The last row on the page.</param>
    /// <returns>The base64url cursor.</returns>
    public static string Encode(FileSummaryRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var json = JsonSerializer.SerializeToUtf8Bytes(
            new CursorDto { V = SchemaVersion, U = row.UpdatedAt, P = row.Project, N = row.Name });
        return Base64Url.EncodeToString(json);
    }

    /// <summary>Decode a cursor into its sort key.</summary>
    /// <param name="cursor">The opaque cursor string.</param>
    /// <returns>The decoded sort key.</returns>
    /// <exception cref="VaultException">
    /// Thrown with <see cref="VaultErrorCode.InvalidArgument"/> (reason <c>cursor_malformed</c>) for
    /// any defect: over-length, bad base64, malformed or too-deep JSON, an unknown version, or a
    /// missing/mistyped field.
    /// </exception>
    public static CursorKey Decode(string cursor)
    {
        try
        {
            // The length gate runs before any decode work, bounding the cost of a hostile input.
            if (cursor is null || cursor.Length > VaultConfig.MaxCursorChars)
            {
                throw Malformed();
            }

            var bytes = Base64Url.DecodeFromChars(cursor);
            var dto = JsonSerializer.Deserialize<CursorDto>(bytes, DecodeOptions);
            return dto is { V: SchemaVersion, U: { } updatedAt, P: { } project, N: { } name }
                ? new CursorKey(updatedAt, project, name)
                : throw Malformed();
        }
        catch (VaultException)
        {
            throw;
        }
        catch (Exception)
        {
            // Catch-all so a raw FormatException/JsonException, whose text could echo the cursor,
            // can never escape into a full internal-error log; every defect collapses to the same
            // opaque invalid_argument.
            throw Malformed();
        }
    }

    private static VaultException Malformed()
        => VaultException.InvalidArgument(Message, "cursor_malformed");

    /// <summary>The wire DTO; strict nullable member types so a missing or mistyped field is detectable.</summary>
    private sealed class CursorDto
    {
        [JsonPropertyName("v")]
        public int? V { get; init; }

        [JsonPropertyName("u")]
        public long? U { get; init; }

        [JsonPropertyName("p")]
        public string P { get; init; }

        [JsonPropertyName("n")]
        public string N { get; init; }
    }
}