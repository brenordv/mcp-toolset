using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;
using RaccoonNinja.McpToolset.Server.GitOps.Security;
using RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Security;

public class EnvironmentBuilderTests
{
    private const string TrackedFile = "tracked.txt";
    private const string TrackedFileContent = "tracked\n";
    private const string TrackedFileCommitMessage = "add tracked file";

    private const string PlantedGitScript = "#!/bin/sh\n: > \"$0.ran\"\nexit 1\n";
    private const string PlantedGitMarker = "git.ran";
    private const string EmptyExecPathDirectory = "empty-exec-path";
    private const string RootRelativeEntry = "\\root-relative";

    private static readonly string FirstPathDirectory = Path.Combine(Path.GetTempPath(), "mcp-gitops-path-first");
    private static readonly string SecondPathDirectory = Path.Combine(Path.GetTempPath(), "mcp-gitops-path-second");

    [Fact]
    public void Build_DropsVariablesNotOnTheAllowlist()
    {
        // Arrange
        var parent = new Dictionary<string, string>
        {
            ["PATH"] = FirstPathDirectory,
            ["GIT_SSH_COMMAND"] = "evil",
            ["GIT_CONFIG_PARAMETERS"] = "evil",
            ["LD_PRELOAD"] = "evil",
            ["RANDOM_THING"] = "x",
        };

        // Act
        var env = EnvironmentBuilder.Build(parent);

        // Assert
        Assert.Equal(FirstPathDirectory, env["PATH"]);
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
    public void Build_WithEmptyAndRelativePathEntries_KeepsFullyQualifiedEntriesInOrder()
    {
        // Arrange
        var parent = new Dictionary<string, string>
        {
            ["PATH"] = string.Join(Path.PathSeparator, string.Empty, FirstPathDirectory, ".", "bin", RootRelativeEntry, SecondPathDirectory, string.Empty),
        };

        // Act
        var env = EnvironmentBuilder.Build(parent);

        // Assert
        Assert.Equal(string.Join(Path.PathSeparator, FirstPathDirectory, SecondPathDirectory), env["PATH"]);
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

    [Fact]
    public async Task Build_WithEmptyPathEntry_StashListStartsNoRepositoryGit()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Git for Windows skips empty PATH entries.");
        }
        else
        {
            // Arrange
            using var repo = new TempGitRepo();
            repo.WriteFile(TrackedFile, TrackedFileContent);
            repo.Run("add", TrackedFile);
            repo.Run("commit", "-m", TrackedFileCommitMessage);
            repo.WriteFile(TrackedFile, "changed\n");
            repo.Run("stash", "push", "-m", "wip");
            var marker = PlantRepositoryGit(repo.RepoPath);
            var emptyExecPath = Directory.CreateDirectory(Path.Combine(repo.RepoPath, ".git", EmptyExecPathDirectory)).FullName;
            var pathWithEmptyEntry = string.Join(Path.PathSeparator, string.Empty, Path.GetDirectoryName(TestGit.Executable));
            var parent = new Dictionary<string, string>
            {
                ["PATH"] = pathWithEmptyEntry,
                ["HOME"] = Environment.GetEnvironmentVariable("HOME"),
            };
            var intent = new GitIntent
            {
                Subcommand = "stash",
                SubSubcommand = "list",
                RepoRoot = repo.RepoPath,
            };
            var (argv, _) = GitCommandBuilder.Build(intent, TestGit.Executable);
            var runner = new GitProcessRunner(NullLogger<GitProcessRunner>.Instance);
            var unfilteredEnv = EnvironmentBuilder.Build(parent);
            unfilteredEnv["PATH"] = pathWithEmptyEntry;
            unfilteredEnv["GIT_EXEC_PATH"] = emptyExecPath;
            await RequirePlantedGitRunsAsync(runner, argv, unfilteredEnv, repo.RepoPath, marker);
            var env = EnvironmentBuilder.Build(parent);
            env["GIT_EXEC_PATH"] = emptyExecPath;

            // Act
            var result = await runner.RunAsync(argv, env, repo.RepoPath, cancellationToken: TestContext.Current.CancellationToken);

            // Assert
            Assert.False(File.Exists(marker));
            Assert.Equal(0, result.ExitCode);
        }
    }

    #region Test Helpers

    /// <summary>
    /// Commit <see cref="TrackedFile"/> in <paramref name="repo"/>, arm the lazy-fetch trap on it, and build the server's
    /// argv and environment for <c>git ls-tree -r -l HEAD</c>, which reads the blob the trap deleted.
    /// </summary>
    /// <returns>The trap's marker file path, and the argv and environment <see cref="GitCommandBuilder.Build"/> returns.</returns>
    private static (string Marker, IList<string> Argv, IDictionary<string, string> Env) ArmLazyFetchTrapForServerRead(TempGitRepo repo)
    {
        repo.WriteFile(TrackedFile, TrackedFileContent);
        repo.Run("add", TrackedFile);
        repo.Run("commit", "-m", TrackedFileCommitMessage);
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

    /// <summary>
    /// Write an executable <c>git</c> script at <paramref name="repoRoot"/> that creates <see cref="PlantedGitMarker"/>
    /// next to itself and exits 1, as a hostile repository could commit one.
    /// </summary>
    /// <returns>The marker file's path. It exists only if something ran the script.</returns>
    private static string PlantRepositoryGit(string repoRoot)
    {
        var script = Path.Combine(repoRoot, "git");
        File.WriteAllText(script, PlantedGitScript);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return Path.Combine(repoRoot, PlantedGitMarker);
    }

    /// <summary>
    /// Run <paramref name="argv"/> with <paramref name="env"/>, the environment whose <c>PATH</c> still has its empty
    /// entry, and fail the test unless the planted script ran, so the calling test cannot pass on a git whose lookup
    /// never reaches the repository root. Delete the marker again.
    /// </summary>
    private static async Task RequirePlantedGitRunsAsync(
        GitProcessRunner runner,
        IList<string> argv,
        IDictionary<string, string> env,
        string repoRoot,
        string marker)
    {
        await runner.RunAsync(argv, env, repoRoot, cancellationToken: TestContext.Current.CancellationToken);
        if (!File.Exists(marker))
        {
            Assert.Fail("git's child lookup did not reach the repository root through the empty PATH entry on this machine, so the trap cannot fire");
        }

        File.Delete(marker);
    }

    #endregion
}