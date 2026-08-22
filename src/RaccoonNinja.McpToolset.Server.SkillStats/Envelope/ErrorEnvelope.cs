using System.Text.Json.Serialization;
using RaccoonNinja.McpToolset.Server.SkillStats.Errors;

namespace RaccoonNinja.McpToolset.Server.SkillStats.Envelope;

/// <summary>The structured <c>error</c> object inside a failure envelope.</summary>
public sealed record ErrorEnvelope
{
    /// <summary>The stable error code (see <see cref="ErrorCodes"/>).</summary>
    [JsonPropertyName("code")]
    public string Code { get; private init; }

    /// <summary>A caller-facing message with no machine-identifying content.</summary>
    [JsonPropertyName("message")]
    public string Message { get; private init; }

    /// <summary>Build an error object from a domain exception.</summary>
    /// <param name="error">The domain exception.</param>
    /// <returns>The error envelope.</returns>
    public static ErrorEnvelope From(SkillStatsException error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ErrorEnvelope
        {
            Code = error.Code,
            Message = error.Message,
        };
    }
}