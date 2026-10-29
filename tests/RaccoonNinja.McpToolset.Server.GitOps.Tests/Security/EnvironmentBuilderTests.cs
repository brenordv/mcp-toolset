using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;
using RaccoonNinja.McpToolset.Server.GitOps.Security;
using RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Security;

public class EnvironmentBuilderTests
{
    private const string TrackedFile = "tracked.txt";

    [Fact]
    public void Build_DropsVariablesNotOnTheAllowlist()
    {
        // Arrange
        var parent = new Dictionary<string, string>
        {
            ["PATH"] = "/usr/bin",
            ["GIT_SSH_COMMAND"] = "evil",
            ["GIT_CONFIG_PARAMETERS"] = "evil",
            ["LD_PRELOAD"] = "evil",
            ["RANDOM_THING"] = "x",
        };

        // Act
        var env = EnvironmentBuilder.Build(parent);

        // Assert
        Assert.Equal("/usr/bin", env["PATH"]);
        Assert.False(env.ContainsKey("GIT_SSH_COMMAND"));
        Assert.False(env.ContainsKey("GIT_CONFIG_PARAMETERS"));
        Assert.False(env.ContainsKey("LD_PRELOAD"));
        Assert.False(env.ContainsKey("RANDOM_THING"));
    }

    [Fact]
    public void Build_AlwaysSetsGitNeutralizers()
    {
        // Act
        var env = EnvironmentBuilder.Build(new Dictionary<string, string>());

        // Assert
        Assert.Equal("0", env["GIT_TERMINAL_PROMPT"]);
        Assert.Equal("1", env["GIT_CONFIG_NOSYSTEM"]);
        Assert.Equal("0", env["GIT_OPTIONAL_LOCKS"]);
        Assert.Equal("1", env["GIT_NO_LAZY_FETCH"]);
        Assert.Equal("none", env["GIT_ALLOW_PROTOCOL"]);
    }

    [Theory]
    [InlineData("GIT_NO_LAZY_FETCH", "0", "1")]
    [InlineData("GIT_ALLOW_PROTOCOL", "file:ssh", "none")]
    public void Build_ParentSetsFixedVariable_KeepsFixedValue(string name, string parentValue, string fixedValue)
    {
        // Arrange
        var parent = new Dictionary<string, string> { [name] = parentValue };

        // Act
        var env = EnvironmentBuilder.Build(parent);

        // Assert
        Assert.Equal(fixedValue, env[name]);
    }

    [Fact]
    public void Build_DoesNotSetLiteralPathspecs()
    {
        // Arrange
        var parent = new Dictionary<string, string> { ["GIT_LITERAL_PATHSPECS"] = "1" };

        // Act
        var env = EnvironmentBuilder.Build(parent);

        // Assert
        Assert.False(env.ContainsKey("GIT_LITERAL_PATHSPECS"));
    }

    [Fact]
    public async Task Build_WithoutLazyFetchVariable_PromisorFetchRunsNoUploadPack()
    {
        // Arrange
        using var repo = new TempGitRepo();
        var (marker, argv, env) = ArmLazyFetchTrapForServerRead(repo);
        env.Remove("GIT_NO_LAZY_FETCH");
        env["LC_ALL"] = "C";
        var runner = new GitProcessRunner(NullLogger<GitProcessRunner>.Instance);

        // Act
        var result = await runner.RunAsync(argv, env, repo.RepoPath, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.False(File.Exists(marker));
        Assert.Contains("transport 'file' not allowed", Encoding.UTF8.GetString(result.Stderr));
    }

    [Fact]
    public async Task Build_WithoutAllowProtocolVariable_PromisorFetchRunsNoUploadPack()
    {
        // Arrange
        using var repo = new TempGitRepo();
        var (marker, argv, env) = ArmLazyFetchTrapForServerRead(repo);
        env.Remove("GIT_ALLOW_PROTOCOL");
        var runner = new GitProcessRunner(NullLogger<GitProcessRunner>.Instance);

        // Act
        await runner.RunAsync(argv, env, repo.RepoPath, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.False(File.Exists(marker));
    }

    #region Test Helpers

    /// <summary>
    /// Commit <see cref="TrackedFile"/> in <paramref name="repo"/>, arm the lazy-fetch trap on it, and build the server's
    /// argv and environment for <c>git ls-tree -r -l HEAD</c>, which reads the blob the trap deleted.
    /// </summary>
    /// <returns>The trap's marker file path, and the argv and environment <see cref="GitCommandBuilder.Build"/> returns.</returns>
    private static (string Marker, IList<string> Argv, IDictionary<string, string> Env) ArmLazyFetchTrapForServerRead(TempGitRepo repo)
    {
        repo.WriteFile(TrackedFile, "tracked\n");
        repo.Run("add", TrackedFile);
        repo.Run("commit", "-m", "add tracked file");
        var marker = repo.ConfigureLazyFetchTrap(TrackedFile);
        var intent = new GitIntent
        {
            Subcommand = "ls-tree",
            RepoRoot = repo.RepoPath,
            Flags = ["-r", "-l"],
            PositionalServerArgs = ["HEAD"],
        };
        var (argv, env) = GitCommandBuilder.Build(intent, TestGit.Executable);
        return (marker, argv, env);
    }

    #endregion
}