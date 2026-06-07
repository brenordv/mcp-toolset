namespace RaccoonNinja.McpToolset.Server.GitOps.Repo;

public interface IRepoRootResolver
{
    Task<RepoRootResolution> ResolveAsync(string cwd, CancellationToken cancellationToken = default);
    void ResetCache();
}

public readonly record struct RepoRootResolution(string Root, bool CacheHit);