using System.Text.Json.Serialization;
using RaccoonNinja.McpToolset.Server.TextEdit.Errors;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Envelope;

/// <summary>The structured <c>error</c> object inside a failure envelope.</summary>
public sealed record ErrorEnvelope
{
    /// <summary>The stable error code (see <see cref="ErrorCodes"/>).</summary>
    [JsonPropertyName("code")]
    public string Code { get; private init; }

    /// <summary>A caller-facing message with no machine-identifying content.</summary>
    [JsonPropertyName("message")]
    public string Message { get; private init; }

    /// <summary>Structured detail; never carries user data or an absolute path.</summary>
    [JsonPropertyName("detail")]
    public IDictionary<string, object> Detail { get; private init; } = new Dictionary<string, object>();

    /// <summary>Build an error object from a domain exception.</summary>
    /// <param name="error">The domain exception.</param>
    /// <returns>The error envelope.</returns>
    public static ErrorEnvelope From(TextEditException error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ErrorEnvelope
        {
            Code = error.Code,
            Message = error.Message,
            Detail = new Dictionary<string, object>(error.Detail),
        };
    }
}