using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;
using RaccoonNinja.McpToolset.Server.GitOps.Security;
using RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Runner;

public sealed class GitProcessRunnerTests
{
    private const string VersionSubcommand = "version";
    private const string HashObjectSubcommand = "hash-object";
    private const string StdinFlag = "--stdin";
    private const string NoFiltersFlag = "--no-filters";
    private const string HelloFile = "hello.txt";
    private const string HelloLine = "hello\n";

    [Fact]
    public async Task RunAsync_WithBareExecutableName_ThrowsGitNotInstalled()
    {
        // Arrange
        var sut = BuildSut();

        // Act
        var act = () => sut.RunAsync(
            [TestGit.BareName, VersionSubcommand],
            EnvironmentBuilder.Build(),
            Path.GetTempPath(),
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<GitNotInstalledException>(act);
    }

    [Fact]
    public async Task RunAsync_WithMissingExecutable_OmitsPathFromMessageAndDetail()
    {
        // Arrange
        var sut = BuildSut();
        var missingExecutable = Path.Combine(Path.GetTempPath(), "git-missing-" + Guid.NewGuid().ToString("N"));

        // Act
        var act = () => sut.RunAsync(
            [missingExecutable, VersionSubcommand],
            EnvironmentBuilder.Build(),
            Path.GetTempPath(),
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<GitNotInstalledException>(act);
        Assert.DoesNotContain(missingExecutable, exception.Message);
        Assert.Equal(TestGit.DisplayName, exception.Detail[TestGit.NotInstalledDetailKey]);
    }

    [Fact]
    public async Task RunAsync_WithStandardInput_GitReadsIt()
    {
        // Arrange
        var sut = BuildSut();
        using var repo = new TempGitRepo();
        repo.WriteFile(HelloFile, HelloLine);
        var expected = repo.Run(HashObjectSubcommand, NoFiltersFlag, HelloFile);

        // Act
        var result = await sut.RunAsync(
            [TestGit.Executable, HashObjectSubcommand, StdinFlag],
            EnvironmentBuilder.Build(),
            repo.RepoPath,
            standardInput: Encoding.UTF8.GetBytes(HelloLine),
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, Encoding.UTF8.GetString(result.Stdout).TrimEnd('\n'));
    }

    #region Test Helpers

    private static GitProcessRunner BuildSut() => new(NullLogger<GitProcessRunner>.Instance);

    #endregion
}