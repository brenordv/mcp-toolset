using System.Text.Json.Serialization;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;

namespace RaccoonNinja.McpToolset.Server.GitOps.Envelope;

/// <summary>Structured <c>error</c> object inside a failure envelope.</summary>
public sealed record ErrorEnvelope
{
    [JsonPropertyName("code")]
    public string Code { get; private init; }

    [JsonPropertyName("message")]
    public string Message { get; private init; }

    [JsonPropertyName("detail")]
    public IDictionary<string, object> Detail { get; private init; } = new Dictionary<string, object>();

    public static ErrorEnvelope From(GitCheckException error) => new()
    {
        Code = error.Code,
        Message = error.Message,
        Detail = new Dictionary<string, object>(error.Detail),
    };
}