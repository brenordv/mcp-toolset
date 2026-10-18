namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;

/// <summary>
/// Creates the shared temp git repository (a <see cref="TempGitRepo"/>) with a known three-commit history.
/// Tests that exercise the runner, repo resolver, or tool methods share it
/// via a collection fixture to amortize setup cost.
/// </summary>
public sealed class GitRepoFixture : IAsyncLifetime
{
    private TempGitRepo _repo;

    /// <summary>The canonical absolute path of the shared repository root.</summary>
    public string RepoPath => _repo.RepoPath;

    public ValueTask InitializeAsync()
    {
        _repo = new TempGitRepo();

        _repo.WriteFile("README.md", "# Test repo\n");
        _repo.Run("add", "README.md");
        _repo.Run("commit", "-m", "initial commit");

        _repo.WriteFile("alpha.txt", "alpha 1\nalpha 2\nalpha 3\n");
        _repo.WriteFile("beta.txt", "beta\n");
        _repo.Run("add", "alpha.txt", "beta.txt");
        _repo.Run("commit", "-m", "add alpha and beta");

        _repo.WriteFile("alpha.txt", "alpha 1\nalpha changed\nalpha 3\n");
        _repo.Run("add", "alpha.txt");
        _repo.Run("commit", "-m", "tweak alpha");

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _repo?.Dispose();
        return ValueTask.CompletedTask;
    }
}