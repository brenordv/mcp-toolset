using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Errors.GitCheckExceptions;

public sealed class GitNotInstalledExceptionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(TestGit.BareName)]
    [InlineData("bin/git")]
    [InlineData("C:git")]
    public void ThrowIfNotFullyQualified_WithNameNotFullyQualified_Throws(string executable)
    {
        // Act
        var act = () => GitNotInstalledException.ThrowIfNotFullyQualified(executable);

        // Assert
        var exception = Assert.Throws<GitNotInstalledException>(act);
        Assert.Equal(TestGit.DisplayName, exception.Detail[TestGit.NotInstalledDetailKey]);
    }

    [Fact]
    public void ThrowIfNotFullyQualified_WithFullyQualifiedPath_DoesNotThrow()
    {
        // Arrange
        var executable = Path.Combine(Path.GetTempPath(), TestGit.BareName);

        // Act
        var exception = Record.Exception(() => GitNotInstalledException.ThrowIfNotFullyQualified(executable));

        // Assert
        Assert.Null(exception);
    }
}