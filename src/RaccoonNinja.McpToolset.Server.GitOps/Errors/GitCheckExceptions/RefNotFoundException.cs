namespace RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;

/// <summary>Raised when a typed <c>ref</c> parameter does not resolve to a git object.</summary>
public sealed class RefNotFoundException(string message, IDictionary<string, object> detail = null)
    : GitCheckException(message, detail)
{
    public override string Code => ErrorCodes.RefNotFound;
}