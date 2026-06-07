namespace RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;

/// <summary>Raised when a typed path argument escapes the resolved repo root (path confinement).</summary>
public sealed class PathOutsideRepoException(string message, IDictionary<string, object> detail = null)
    : GitCheckException(message, detail)
{
    public override string Code => ErrorCodes.PathOutsideRepo;
}