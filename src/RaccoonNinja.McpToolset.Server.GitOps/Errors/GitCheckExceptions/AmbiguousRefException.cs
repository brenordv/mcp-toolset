namespace RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;

/// <summary>Raised when a typed <c>ref</c> parameter resolves to more than one git object.</summary>
public sealed class AmbiguousRefException(string message, IDictionary<string, object> detail = null)
    : GitCheckException(message, detail)
{
    public override string Code => ErrorCodes.AmbiguousRef;
}