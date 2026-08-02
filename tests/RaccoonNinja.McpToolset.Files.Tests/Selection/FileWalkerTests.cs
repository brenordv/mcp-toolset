using RaccoonNinja.McpToolset.Files.Security;
using RaccoonNinja.McpToolset.Files.Selection;

namespace RaccoonNinja.McpToolset.Files.Tests.Selection;

public sealed class FileWalkerTests : IDisposable
{
    private readonly string _rootDir;
    private readonly FileWalker _walker;
    private readonly List<string> _cleanup = [];

    public FileWalkerTests()
    {
        _rootDir = NewTempDirectory("walkroot");
        _walker = new FileWalker(new RootConfinement(_rootDir), new SecretDenylist());
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
    public void Walk_FilesOnlyByDefault_SortedOrdinalRootRelative()
    {
        // Arrange
        Write("b.txt", "b");
        Write("a.txt", "a");
        Write("sub/c.txt", "c");

        // Act
        var result = _walker.Walk(new FileWalkOptions());

        // Assert
        Assert.Equal(["a.txt", "b.txt", "sub/c.txt"], Paths(result));
        Assert.All(result.Entries, entry => Assert.False(entry.IsDirectory));
        Assert.False(result.Truncated);
        Assert.Equal(0, result.SkippedSymlinks);
    }

    [Fact]
    public void Walk_IncludeDirectories_ReturnsSurvivingDirectories()
    {
        // Arrange
        Write("sub/c.txt", "c");

        // Act
        var result = _walker.Walk(new FileWalkOptions { IncludeDirectories = true });

        // Assert
        Assert.Contains("sub", Paths(result));
        Assert.Contains("sub/c.txt", Paths(result));
    }

    [Fact]
    public void Walk_ReportsFileMetadata()
    {
        // Arrange
        Write("data.bin", "12345");

        // Act
        var entry = Assert.Single(_walker.Walk(new FileWalkOptions()).Entries);

        // Assert
        Assert.Equal("data.bin", entry.RelativePath);
        Assert.Equal(5, entry.Size);
        Assert.True(entry.LastModifiedUtc <= DateTimeOffset.UtcNow.AddMinutes(1));
    }

    [Fact]
    public void Walk_OmitsDenylistedFilesAndNeverDescendsDenylistedDirectories()
    {
        // Arrange
        Write("app.cs", "code");
        Write(".env", "SECRET=1");
        Write("server.pem", "key");
        Write(".git/config", "[core]");
        Write(".ssh/id_rsa", "private");

        // Act
        var paths = Paths(_walker.Walk(new FileWalkOptions()));

        // Assert
        Assert.Equal(["app.cs"], paths);
    }

    [Fact]
    public void Walk_AppliesIgnoreRules_AndPrunesIgnoredDirectories()
    {
        // Arrange
        Write(".gitignore", "*.log\nbuild/\n");
        Write("keep.txt", "k");
        Write("app.log", "l");
        Write("build/output.txt", "o");

        // Act
        var paths = Paths(_walker.Walk(new FileWalkOptions()));

        // Assert
        Assert.Equal([".gitignore", "keep.txt"], paths);
    }

    [Fact]
    public void Walk_IncludeIgnored_ReturnsIgnoredEntries()
    {
        // Arrange
        Write(".gitignore", "*.log\n");
        Write("keep.txt", "k");
        Write("app.log", "l");

        // Act
        var paths = Paths(_walker.Walk(new FileWalkOptions { IncludeIgnored = IncludeGlobSet.Compile(["**"]) }));

        // Assert
        Assert.Contains("app.log", paths);
    }

    [Fact]
    public void Walk_DefaultIgnoreTier_PrunesWithoutProjectIgnoreFile()
    {
        // Arrange
        Write("node_modules/lib.js", "x");
        Write("src/app.js", "y");
        var walker = new FileWalker(new RootConfinement(_rootDir), new SecretDenylist(), IgnoreRules.Parse(["node_modules/"]));

        // Act
        var paths = Paths(walker.Walk(new FileWalkOptions()));

        // Assert
        Assert.Equal(["src/app.js"], paths);
    }

    [Fact]
    public void Walk_ProjectGitignoreNegation_OverridesDefaultTier()
    {
        // Arrange
        Write("build/out.txt", "o");
        Write(".gitignore", "!build/\n");
        var walker = new FileWalker(new RootConfinement(_rootDir), new SecretDenylist(), IgnoreRules.Parse(["build/"]));

        // Act
        var paths = Paths(walker.Walk(new FileWalkOptions()));

        // Assert
        Assert.Contains("build/out.txt", paths);
    }

    [Fact]
    public void Walk_IncludeGlob_ReachesNestedIgnoredFileWithoutSurfacingSiblings()
    {
        // Arrange
        Write("node_modules/lodash/index.js", "a");
        Write("node_modules/react/index.js", "b");
        Write("bin/tool.txt", "c");
        Write("src/app.js", "d");
        var walker = new FileWalker(new RootConfinement(_rootDir), new SecretDenylist(), IgnoreRules.Parse(["node_modules/", "bin/"]));

        // Act
        var paths = Paths(walker.Walk(new FileWalkOptions { IncludeIgnored = IncludeGlobSet.Compile(["node_modules/lodash/**"]) }));

        // Assert
        Assert.Contains("node_modules/lodash/index.js", paths);
        Assert.DoesNotContain("node_modules/react/index.js", paths);
        Assert.DoesNotContain("bin/tool.txt", paths);
        Assert.Contains("src/app.js", paths);
    }

    [Fact]
    public void Walk_IncludeGlob_NeverReIncludesDenylistedDirectory()
    {
        // Arrange
        Write(".git/config", "secret");
        Write("src/app.js", "ok");
        var walker = new FileWalker(new RootConfinement(_rootDir), new SecretDenylist(), IgnoreRules.Parse(["node_modules/"]));

        // Act
        var paths = Paths(walker.Walk(new FileWalkOptions { IncludeIgnored = IncludeGlobSet.Compile([".git/**", "**"]) }));

        // Assert
        Assert.DoesNotContain(".git/config", paths);
        Assert.Contains("src/app.js", paths);
    }

    [Fact]
    public void Walk_IncludeAll_ReIncludesIgnoredFilesButNotDenylisted()
    {
        // Arrange
        Write("node_modules/pkg/main.js", "m");
        Write(".env", "SECRET=1");
        var walker = new FileWalker(new RootConfinement(_rootDir), new SecretDenylist(), IgnoreRules.Parse(["node_modules/"]));

        // Act
        var paths = Paths(walker.Walk(new FileWalkOptions { IncludeIgnored = IncludeGlobSet.Compile(["**"]) }));

        // Assert
        Assert.Contains("node_modules/pkg/main.js", paths);
        Assert.DoesNotContain(".env", paths);
    }

    [Fact]
    public void Walk_NestedGitignore_AppliesUnderItsDirectory()
    {
        // Arrange
        Write("sub/.gitignore", "*.tmp\n");
        Write("sub/a.tmp", "t");
        Write("sub/a.txt", "x");
        Write("top.tmp", "t");

        // Act
        var paths = Paths(_walker.Walk(new FileWalkOptions()));

        // Assert
        Assert.Contains("sub/a.txt", paths);
        Assert.Contains("top.tmp", paths);
        Assert.DoesNotContain("sub/a.tmp", paths);
    }

    [Fact]
    public void Walk_SkipsAndCountsSymlinkedDirectory_WithoutDescending()
    {
        // Arrange
        var outside = NewTempDirectory("walk-outside");
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "leak");
        Write("real.txt", "r");
        if (!TryCreateDirectoryLink(Path.Combine(_rootDir, "linkdir"), outside))
        {
            Assert.Skip("Creating a directory link is not permitted in this environment.");
        }

        // Act
        var result = _walker.Walk(new FileWalkOptions());

        // Assert
        Assert.Equal(["real.txt"], Paths(result));
        Assert.Equal(1, result.SkippedSymlinks);
    }

    [Fact]
    public void Walk_AppliesMatchPredicateAfterPruning()
    {
        // Arrange
        Write("a.cs", "1");
        Write("b.txt", "2");
        Write("c.cs", "3");

        // Act
        var paths = Paths(_walker.Walk(new FileWalkOptions
        {
            Match = path => path.EndsWith(".cs", StringComparison.Ordinal),
        }));

        // Assert
        Assert.Equal(["a.cs", "c.cs"], paths);
    }

    [Fact]
    public void Walk_MaxResults_TruncatesAfterSorting()
    {
        // Arrange
        for (var i = 0; i < 5; i++)
        {
            Write($"f{i}.txt", "x");
        }

        // Act
        var result = _walker.Walk(new FileWalkOptions { MaxResults = 2 });

        // Assert
        Assert.Equal(["f0.txt", "f1.txt"], Paths(result));
        Assert.True(result.Truncated);
    }

    [Fact]
    public void Walk_MaxVisitedNodes_StopsAndMarksTruncated()
    {
        // Arrange
        for (var i = 0; i < 5; i++)
        {
            Write($"f{i}.txt", "x");
        }

        // Act
        var result = _walker.Walk(new FileWalkOptions { MaxVisitedNodes = 1 });

        // Assert
        Assert.True(result.Truncated);
        Assert.True(result.Entries.Count <= 1);
    }

    [Fact]
    public void Walk_StartSubdirectory_ScopesToThatBranch()
    {
        // Arrange
        Write("sub/x.txt", "x");
        Write("y.txt", "y");

        // Act
        var paths = Paths(_walker.Walk(new FileWalkOptions { Start = "sub" }));

        // Assert
        Assert.Equal(["sub/x.txt"], paths);
    }

    [Fact]
    public void Walk_NonexistentStart_Throws()
    {
        // Arrange
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _walker.Walk(new FileWalkOptions { Start = "missing" }));
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

    private static bool TryCreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (IOException)
        {
            return TryCreateJunctionOnWindows(link, target);
        }
        catch (UnauthorizedAccessException)
        {
            return TryCreateJunctionOnWindows(link, target);
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static bool TryCreateJunctionOnWindows(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = System.Diagnostics.Process.Start(startInfo);
            process.WaitForExit();
            return process.ExitCode == 0 && Directory.Exists(link);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}