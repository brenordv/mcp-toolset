namespace RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;

/// <summary>Raised when a typed path argument does not exist inside the resolved repo.</summary>
public sealed class PathNotFoundException(string message, IDictionary<string, object> detail = null)
    : GitCheckException(message, detail)
{
    public override string Code => ErrorCodes.PathNotFound;
}