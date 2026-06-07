namespace RaccoonNinja.McpToolset.Server.GitOps.Repo;

public interface IRefVerifier
{
    /// <summary>Resolve <paramref name="reference"/> via <c>git rev-parse --verify --end-of-options</c>; returns the SHA.</summary>
    Task<string> VerifyAsync(string reference, string repoRoot, CancellationToken cancellationToken = default);
}