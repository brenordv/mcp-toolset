using RaccoonNinja.McpToolset.Server.FileVault.Security;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tests.Security;

public sealed class GitExecutableLocatorTests : IDisposable
{
    private const UnixFileMode ExecutableMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode NonExecutableMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private readonly string _tempRoot;
    private readonly string _relativeDirectoryName;
    private readonly string _relativeDirectory;

    public GitExecutableLocatorTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "mcp-filevault-locator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _relativeDirectoryName = "mcp-filevault-locator-relative-" + Guid.NewGuid().ToString("N");
        _relativeDirectory = Path.Combine(Directory.GetCurrentDirectory(), _relativeDirectoryName);
        Directory.CreateDirectory(_relativeDirectory);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
        Directory.Delete(_relativeDirectory, recursive: true);
    }

    [Fact]
    public void Find_WithRelativeAndAbsoluteEntries_ReturnsFirstAbsoluteMatch()
    {
        // Arrange
        WriteFakeGit(_relativeDirectory, ExecutableMode);
        var first = NewDirectory();
        WriteFakeGit(first, ExecutableMode);
        var second = NewDirectory();
        WriteFakeGit(second, ExecutableMode);
        var pathVariable = string.Join(Path.PathSeparator, string.Empty, _relativeDirectoryName, first, second);

        // Act
        var found = GitExecutableLocator.Find(pathVariable);

        // Assert
        Assert.Equal(Path.Combine(first, GitFileName), found);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Find_WithBlankPath_ReturnsNull(string pathVariable)
    {
        // Act
        var found = GitExecutableLocator.Find(pathVariable);

        // Assert
        Assert.Null(found);
    }

    [Fact]
    public void Find_WithNoGitOnPath_ReturnsNull()
    {
        // Arrange
        var empty = NewDirectory();

        // Act
        var found = GitExecutableLocator.Find(empty);

        // Assert
        Assert.Null(found);
    }

    [Fact]
    public void Find_WithDirectoryNamedGit_SkipsIt()
    {
        // Arrange
        var first = NewDirectory();
        Directory.CreateDirectory(Path.Combine(first, GitFileName));
        var second = NewDirectory();
        WriteFakeGit(second, ExecutableMode);

        // Act
        var found = GitExecutableLocator.Find(string.Join(Path.PathSeparator, first, second));

        // Assert
        Assert.Equal(Path.Combine(second, GitFileName), found);
    }

    [Fact]
    public void Find_WithGitWithoutExecuteBit_SkipsIt()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes do not apply on Windows.");
        }
        else
        {
            // Arrange
            var first = NewDirectory();
            WriteFakeGit(first, NonExecutableMode);
            var second = NewDirectory();
            WriteFakeGit(second, ExecutableMode);

            // Act
            var found = GitExecutableLocator.Find(string.Join(Path.PathSeparator, first, second));

            // Assert
            Assert.Equal(Path.Combine(second, GitFileName), found);
        }
    }

    [Fact]
    public void Find_WithRootRelativeEntry_SkipsIt()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Root-relative paths are a Windows concept.");
        }

        // Arrange
        WriteFakeGit(_relativeDirectory, ExecutableMode);
        var rootRelative = _relativeDirectory[2..];
        var second = NewDirectory();
        WriteFakeGit(second, ExecutableMode);

        // Act
        var found = GitExecutableLocator.Find(string.Join(Path.PathSeparator, rootRelative, second));

        // Assert
        Assert.Equal(Path.Combine(second, GitFileName), found);
    }

    #region Test Helpers

    private static string GitFileName => OperatingSystem.IsWindows() ? "git.exe" : "git";

    private string NewDirectory()
    {
        var directory = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void WriteFakeGit(string directory, UnixFileMode mode)
    {
        var path = Path.Combine(directory, GitFileName);
        File.WriteAllBytes(path, []);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, mode);
        }
    }

    #endregion
}