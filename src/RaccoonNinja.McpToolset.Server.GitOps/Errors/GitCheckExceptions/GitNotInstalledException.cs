namespace RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;

/// <summary>Raised when the configured <c>git</c> executable is not present or unreachable.</summary>
public sealed class GitNotInstalledException(string message, IDictionary<string, object> detail = null)
    : GitCheckException(message, detail)
{
    public override string Code => ErrorCodes.GitNotInstalled;
}