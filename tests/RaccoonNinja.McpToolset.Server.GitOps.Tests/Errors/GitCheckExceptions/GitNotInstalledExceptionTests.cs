using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Errors.GitCheckExceptions;

public sealed class GitNotInstalledExceptionTests
{
    private const string BareExecutableName = "git";
    private const string ExecutableDetailKey = "executable";
    private const string ExecutableDisplayName = "git";

    [Theory]
    [InlineData(null)]
    [InlineData(BareExecutableName)]
    [InlineData("bin/git")]
    [InlineData("C:git")]
    public void ThrowIfNotFullyQualified_WithNameNotFullyQualified_Throws(string executable)
    {
        // Act
        var act = () => GitNotInstalledException.ThrowIfNotFullyQualified(executable);

        // Assert
        var exception = Assert.Throws<GitNotInstalledException>(act);
        Assert.Equal(ExecutableDisplayName, exception.Detail[ExecutableDetailKey]);
    }

    [Fact]
    public void ThrowIfNotFullyQualified_WithFullyQualifiedPath_DoesNotThrow()
    {
        // Arrange
        var executable = Path.Combine(Path.GetTempPath(), BareExecutableName);

        // Act
        var exception = Record.Exception(() => GitNotInstalledException.ThrowIfNotFullyQualified(executable));

        // Assert
        Assert.Null(exception);
    }
}