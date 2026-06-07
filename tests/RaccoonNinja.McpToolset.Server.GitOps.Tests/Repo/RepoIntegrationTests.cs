using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Repo;

[Collection(nameof(GitRepoCollection))]
public class RepoIntegrationTests
{
    private readonly GitRepoFixture _fixture;

    public RepoIntegrationTests(GitRepoFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RepoRootResolver_Resolves_Fixture_Repo()
    {
        var resolver = new RepoRootResolver();
        resolver.ResetCache();
        var first = await resolver.ResolveAsync(_fixture.RepoPath);
        var second = await resolver.ResolveAsync(_fixture.RepoPath);
        Assert.False(first.CacheHit);
        Assert.True(second.CacheHit);
        Assert.Equal(Path.GetFullPath(_fixture.RepoPath), Path.GetFullPath(first.Root));
    }

    [Fact]
    public async Task RepoRootResolver_Rejects_Relative_Cwd()
    {
        var resolver = new RepoRootResolver();
        await Assert.ThrowsAsync<RejectedArgumentException>(async () =>
            await resolver.ResolveAsync("relative-path"));
    }

    [Fact]
    public async Task RepoRootResolver_Throws_When_Cwd_Is_Not_A_Repo()
    {
        var temp = Path.Combine(Path.GetTempPath(), "non-repo-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var resolver = new RepoRootResolver();
            resolver.ResetCache();
            await Assert.ThrowsAsync<NotAGitRepositoryException>(async () =>
                await resolver.ResolveAsync(temp));
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public async Task RefVerifier_Resolves_Head_To_Sha()
    {
        var verifier = new RefVerifier();
        var sha = await verifier.VerifyAsync("HEAD", _fixture.RepoPath);
        Assert.Equal(40, sha.Length);
    }

    [Fact]
    public async Task RefVerifier_Throws_For_Bad_Ref()
    {
        var verifier = new RefVerifier();
        await Assert.ThrowsAsync<RefNotFoundException>(async () =>
            await verifier.VerifyAsync("does-not-exist-zzz", _fixture.RepoPath));
    }

    [Fact]
    public async Task RefVerifier_Rejects_Empty_And_Dash_Prefixed()
    {
        var verifier = new RefVerifier();
        await Assert.ThrowsAsync<RejectedArgumentException>(async () =>
            await verifier.VerifyAsync(string.Empty, _fixture.RepoPath));
        await Assert.ThrowsAsync<RejectedArgumentException>(async () =>
            await verifier.VerifyAsync("-evil", _fixture.RepoPath));
    }
}