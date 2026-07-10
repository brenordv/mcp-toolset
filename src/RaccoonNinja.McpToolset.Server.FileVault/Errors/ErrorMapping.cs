using System.Text.Json.Nodes;

namespace RaccoonNinja.McpToolset.Server.FileVault.Errors;

/// <summary>
/// Renders domain failures as a structured error body: the tool call fails (<c>isError</c>) and
/// its text content is a single JSON object carrying the stable <c>code</c>, the Rust-parity
/// display message, and the per-code payload fields the Rust server put in the protocol error's
/// <c>data</c>. (The Rust server surfaced these as protocol errors; here they ride inside the
/// failed tool result instead.)
/// </summary>
public static class ErrorMapping
{
    /// <summary>Render the error body for a domain failure.</summary>
    /// <param name="exception">The domain exception.</param>
    /// <returns>The single-line JSON error body.</returns>
    public static string ToErrorJson(VaultException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var error = new JsonObject
        {
            ["code"] = exception.Code.ToWireCode(),
            ["message"] = exception.Message,
        };

        switch (exception.Code)
        {
            case VaultErrorCode.NotFound or VaultErrorCode.Archived:
                error["project"] = exception.Project;
                error["name"] = exception.Name;
                break;

            case VaultErrorCode.Conflict:
                error["current_version"] = exception.CurrentVersion;
                if (exception.Diff is not null)
                {
                    error["base_version"] = exception.BaseVersion;
                    error["diff"] = exception.Diff;
                }

                break;

            case VaultErrorCode.AmbiguousProject:
                error["tried"] = new JsonArray([.. (exception.Tried ?? []).Select(t => (JsonNode)t)]);
                break;

            case VaultErrorCode.ParentNotFound:
                error["project"] = exception.Project;
                error["parent"] = exception.Parent;
                break;

            default:
                break;
        }

        return new JsonObject { ["error"] = error }.ToJsonString();
    }

    /// <summary>
    /// Render the generic internal-error body. Only the exception type name is exposed; the full
    /// exception is logged server-side.
    /// </summary>
    /// <param name="exception">The unexpected failure.</param>
    /// <returns>The single-line JSON error body.</returns>
    public static string ToInternalErrorJson(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var error = new JsonObject
        {
            ["code"] = "internal",
            ["message"] = $"internal error: {exception.GetType().Name}",
        };
        return new JsonObject { ["error"] = error }.ToJsonString();
    }
}