namespace RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;

/// <summary>Raised when a git child process exceeds the configured timeout.</summary>
public sealed class GitTimeoutException(string message, IDictionary<string, object> detail = null)
    : GitCheckException(message, detail)
{
    public override string Code => ErrorCodes.GitTimeout;
}