namespace RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;

/// <summary>Raised when the supplied cwd does not resolve to a usable git repository.</summary>
public sealed class NotAGitRepositoryException(string message, IDictionary<string, object> detail = null)
    : GitCheckException(message, detail)
{
    public override string Code => ErrorCodes.NotAGitRepository;
}