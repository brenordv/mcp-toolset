using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;
using RaccoonNinja.McpToolset.Server.GitOps.Security;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Runner;

public sealed class GitProcessRunnerTests
{
    private const string BareExecutableName = "git";
    private const string VersionSubcommand = "version";
    private const string ExecutableDetailKey = "executable";
    private const string ExecutableDisplayName = "git";

    [Fact]
    public async Task RunAsync_WithBareExecutableName_ThrowsGitNotInstalled()
    {
        // Arrange
        var sut = BuildSut();

        // Act
        var act = () => sut.RunAsync(
            [BareExecutableName, VersionSubcommand],
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
        Assert.Equal(ExecutableDisplayName, exception.Detail[ExecutableDetailKey]);
    }

    #region Test Helpers

    private static GitProcessRunner BuildSut() => new(NullLogger<GitProcessRunner>.Instance);

    #endregion
}