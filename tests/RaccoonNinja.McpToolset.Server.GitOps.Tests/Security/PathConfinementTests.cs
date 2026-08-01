using System.Runtime.InteropServices;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Security;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Security;

public class PathConfinementTests
{
    [Fact]
    public void Confine_ReturnsPosixRelativePathForSubdir()
    {
        // Arrange
        var root = Path.Combine(Path.GetTempPath(), "confine-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "src"));

        // Act
        var rel = PathConfinement.Confine(root, "src/foo.cs");

        // Assert
        Assert.Equal("src/foo.cs", rel);
    }

    [Fact]
    public void Confine_ReturnsDotForRootItself()
    {
        // Arrange
        var root = Path.Combine(Path.GetTempPath(), "confine-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        // Act & Assert
        Assert.Equal(".", PathConfinement.Confine(root, "."));
    }

    [Fact]
    public void Confine_RejectsPathThatEscapesRoot()
    {
        // Arrange
        var root = Path.Combine(Path.GetTempPath(), "confine-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        // Act & Assert
        Assert.Throws<PathOutsideRepoException>(() => PathConfinement.Confine(root, "../escape"));
    }

    [Fact]
    public void Confine_RejectsUncPath()
    {
        // Act & Assert
        Assert.Throws<PathOutsideRepoException>(() => PathConfinement.Confine(@"C:\anything", @"\\server\share"));
    }

    [Fact]
    public void Confine_RejectsPathStartingWithDash()
    {
        // Act & Assert
        Assert.Throws<RejectedArgumentException>(() => PathConfinement.Confine(@"C:\anything", "-evil"));
    }

    [Fact]
    public void Confine_RejectsAlternateDataStreamOnWindows()
    {
        // Arrange
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        // Act & Assert
        Assert.Throws<PathOutsideRepoException>(() => PathConfinement.Confine(@"C:\anything", @"foo:bar"));
    }
}