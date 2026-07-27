using RaccoonNinja.McpToolset.Files.Security;
using RaccoonNinja.McpToolset.Files.Selection;

namespace RaccoonNinja.McpToolset.Files.Tests.Selection;

public sealed class FileSelectionTests : IDisposable
{
    private readonly string _rootDir;
    private readonly FileSelection _selection;
    private readonly List<string> _cleanup = [];

    public FileSelectionTests()
    {
        _rootDir = NewTempDirectory("selroot");
        _selection = new FileSelection(new RootConfinement(_rootDir), new SecretDenylist());
    }

    public void Dispose()
    {
        foreach (var dir in _cleanup)
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup.
            }
        }
    }

    [Fact]
    public void Select_GlobMode_ReturnsMatchingFiles()
    {
        // Arrange
        Write("src/a.cs", "1");
        Write("src/b.txt", "2");
        Write("test/c.cs", "3");

        // Act
        var paths = Paths(_selection.Select(FileSelector.Create(glob: "**/*.cs")));

        // Assert
        Assert.Equal(["src/a.cs", "test/c.cs"], paths);
    }

    [Fact]
    public void Select_RegexMode_ReturnsMatchingFiles()
    {
        // Arrange
        Write("UserTest.cs", "1");
        Write("User.cs", "2");

        // Act
        var paths = Paths(_selection.Select(FileSelector.Create(regex: ".*Test\\.cs")));

        // Assert
        Assert.Equal(["UserTest.cs"], paths);
    }

    [Fact]
    public void Select_AllMode_ReturnsEverythingPrunedByDenylistAndIgnore()
    {
        // Arrange
        Write(".gitignore", "*.log\n");
        Write("keep.cs", "1");
        Write("skip.log", "2");
        Write(".env", "SECRET=1");

        // Act
        var paths = Paths(_selection.Select(FileSelector.Create()));

        // Assert
        Assert.Equal([".gitignore", "keep.cs"], paths);
    }

    [Fact]
    public void Select_IncludeIgnored_BypassesIgnoreButNotDenylist()
    {
        // Arrange
        Write(".gitignore", "*.log\n");
        Write("skip.log", "2");
        Write(".env", "SECRET=1");

        // Act
        var paths = Paths(_selection.Select(FileSelector.Create(includeIgnored: true)));

        // Assert - the ignored log surfaces, the denylisted .env still does not.
        Assert.Contains("skip.log", paths);
        Assert.DoesNotContain(".env", paths);
    }

    [Fact]
    public void Select_GlobMode_MaxFilesCapsAndMarksTruncated()
    {
        // Arrange
        for (var i = 0; i < 5; i++)
        {
            Write($"f{i}.cs", "x");
        }

        // Act
        var result = _selection.Select(FileSelector.Create(glob: "*.cs", maxFiles: 2));

        // Assert
        Assert.Equal(2, result.Entries.Count);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void Select_PathsMode_ReturnsNamedFilesWithMetadata()
    {
        // Arrange
        Write("a.cs", "hello");
        Write("sub/b.cs", "hi");

        // Act
        var result = _selection.Select(FileSelector.Create(paths: ["a.cs", "sub/b.cs"]));

        // Assert
        Assert.Equal(["a.cs", "sub/b.cs"], Paths(result));
        Assert.Equal(5, result.Entries[0].Size);
    }

    [Fact]
    public void Select_PathsMode_ReadGateOmitsDenylistedPaths()
    {
        // Arrange - the read gate must hold even when enumeration is skipped (ADR C3).
        Write("ok.cs", "1");
        Write(".env", "SECRET=1");
        Write(".git/config", "[core]");
        Write(".ssh/id_rsa", "private");

        // Act
        var paths = Paths(_selection.Select(FileSelector.Create(
            paths: ["ok.cs", ".env", ".git/config", ".ssh/id_rsa"])));

        // Assert
        Assert.Equal(["ok.cs"], paths);
    }

    [Fact]
    public void Select_PathsMode_OmitsOutOfRootPath()
    {
        // Arrange
        var outside = NewTempDirectory("sel-outside");
        var secret = Path.Combine(outside, "secret.txt");
        File.WriteAllText(secret, "leak");
        Write("ok.cs", "1");

        // Act
        var paths = Paths(_selection.Select(FileSelector.Create(paths: ["ok.cs", secret])));

        // Assert
        Assert.Equal(["ok.cs"], paths);
    }

    [Fact]
    public void Select_PathsMode_OmitsDirectoriesAndMissingPaths()
    {
        // Arrange
        Write("sub/x.cs", "1");
        Write("real.cs", "2");

        // Act
        var paths = Paths(_selection.Select(FileSelector.Create(
            paths: ["real.cs", "sub", "does/not/exist.cs"])));

        // Assert - only the real file survives; the directory and the missing path are omitted.
        Assert.Equal(["real.cs"], paths);
    }

    [Fact]
    public void Select_PathsMode_MaxFilesCapsAndMarksTruncated()
    {
        // Arrange
        Write("a.cs", "1");
        Write("b.cs", "2");
        Write("c.cs", "3");

        // Act
        var result = _selection.Select(FileSelector.Create(paths: ["a.cs", "b.cs", "c.cs"], maxFiles: 2));

        // Assert
        Assert.Equal(["a.cs", "b.cs"], Paths(result));
        Assert.True(result.Truncated);
    }

    private static string[] Paths(WalkResult result)
        => result.Entries.Select(entry => entry.RelativePath).ToArray();

    private void Write(string relativePath, string content)
    {
        var full = Path.Combine(_rootDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, content);
    }

    private string NewTempDirectory(string label)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"rnmcp-{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _cleanup.Add(dir);
        return dir;
    }
}