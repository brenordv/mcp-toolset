using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;
using RaccoonNinja.McpToolset.Server.GitOps.Security;
using RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Runner;

public sealed class GitProcessRunnerTests
{
    private const string VersionSubcommand = "version";

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

    #region Test Helpers

    private static GitProcessRunner BuildSut() => new(NullLogger<GitProcessRunner>.Instance);

    #endregion
}