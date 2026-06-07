namespace RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;

/// <summary>
/// Non-zero git exit not otherwise classified. The envelope <c>detail.stderr_tail</c>
/// carries a length-capped, control-char-stripped tail of git's stderr.
/// </summary>
public sealed class GitCommandException(string message, IDictionary<string, object> detail = null)
    : GitCheckException(message, detail)
{
    public override string Code => ErrorCodes.GitCommandError;
}