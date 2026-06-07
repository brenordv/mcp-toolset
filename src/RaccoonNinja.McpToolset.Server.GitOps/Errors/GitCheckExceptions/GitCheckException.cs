namespace RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;

/// <summary>
/// Base type for every domain-level error this server emits. Domain errors travel
/// inside the structured envelope as a populated <c>error</c> object. They are
/// NOT raised through MCP's protocol error channel.
/// </summary>
public class GitCheckException(string message, IDictionary<string, object> detail = null) : Exception(message)
{
    /// <summary>Stable taxonomy code (also surfaces as the log <c>error_code</c>).</summary>
    public virtual string Code => ErrorCodes.GitCheckError;

    /// <summary>Optional structured detail dict consumed verbatim by the envelope mapper.</summary>
    public IDictionary<string, object> Detail { get; } = detail ?? new Dictionary<string, object>();
}