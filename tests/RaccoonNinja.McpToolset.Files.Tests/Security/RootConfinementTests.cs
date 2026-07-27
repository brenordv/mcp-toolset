using RaccoonNinja.McpToolset.Files.Security;

namespace RaccoonNinja.McpToolset.Files.Tests.Security;

public sealed class RootConfinementTests : IDisposable
{
    private readonly string _rootDir;
    private readonly RootConfinement _confiner;
    private readonly List<string> _cleanup = [];

    public RootConfinementTests()
    {
        _rootDir = NewTempDirectory("root");
        _confiner = new RootConfinement(_rootDir);
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
                // Best-effort cleanup; a leftover temp dir is harmless.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup; a leftover temp dir is harmless.
            }
        }
    }

    [Fact]
    public void Confine_RootItself_ResolvesToDot()
    {
        // Arrange
        // Act
        var confined = _confiner.Confine(".");

        // Assert
        Assert.Equal(".", confined.RelativePath);
        Assert.True(confined.Exists);
        Assert.Equal(_confiner.CanonicalRoot, confined.RealPath);
    }

    [Fact]
    public void Confine_ExistingNestedFile_ReturnsRootRelativePosixPath()
    {
        // Arrange
        CreateFileUnderRoot("sub/file.txt");

        // Act
        var confined = _confiner.Confine("sub/file.txt");

        // Assert
        Assert.Equal("sub/file.txt", confined.RelativePath);
        Assert.True(confined.Exists);
        Assert.StartsWith(_confiner.CanonicalRoot, confined.RealPath, StringComparison.Ordinal);
    }

    [Fact]
    public void Confine_NotYetCreatedLeaf_IsAllowedAndMarkedMissing()
    {
        // Arrange
        // Act
        var confined = _confiner.Confine("newdir/new.txt");

        // Assert
        Assert.Equal("newdir/new.txt", confined.RelativePath);
        Assert.False(confined.Exists);
    }

    [Fact]
    public void Confine_ParentTraversalEscape_Throws()
    {
        // Arrange
        // Act
        // Assert
        Assert.Throws<PathConfinementException>(() => _confiner.Confine("../escape.txt"));
    }

    [Fact]
    public void Confine_AbsolutePathOutsideRoot_Throws()
    {
        // Arrange
        var outside = NewTempDirectory("outside");

        // Act
        // Assert
        Assert.Throws<PathConfinementException>(() => _confiner.Confine(outside));
    }

    [Theory]
    [InlineData(@"\\server\share")]
    [InlineData("//server/share")]
    [InlineData(@"\\?\C:\Windows")]
    [InlineData(@"\\.\PhysicalDrive0")]
    [InlineData("C:relative")]
    public void Confine_HostileSyntax_Throws(string candidate)
    {
        // Arrange
        // Act
        // Assert
        Assert.Throws<PathConfinementException>(() => _confiner.Confine(candidate));
    }

    [Fact]
    public void Confine_AlternateDataStream_ThrowsOnWindows()
    {
        // Arrange
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Alternate data streams are an NTFS/Windows concept.");
        }

        // Act
        // Assert
        Assert.Throws<PathConfinementException>(() => _confiner.Confine("notes.txt:$DATA"));
    }

    [Fact]
    public void Confine_NulByte_Throws()
    {
        // Arrange
        // Act
        // Assert
        Assert.Throws<PathConfinementException>(() => _confiner.Confine("na\0me.txt"));
    }

    [Fact]
    public void Confine_NullCandidate_Throws()
    {
        // Arrange
        // Act
        // Assert
        Assert.Throws<ArgumentNullException>(() => _confiner.Confine(null));
    }

    [Fact]
    public void Confine_SymlinkLeafOutOfTree_Throws()
    {
        // Arrange
        var outside = NewTempDirectory("outside");
        var secret = Path.Combine(outside, "secret.txt");
        File.WriteAllText(secret, "top secret");
        var link = Path.Combine(_rootDir, "leak.txt");
        if (!TryCreateFileLink(link, secret))
        {
            Assert.Skip("Creating symbolic links is not permitted in this environment.");
        }

        // Act
        // Assert
        Assert.Throws<PathConfinementException>(() => _confiner.Confine("leak.txt"));
    }

    [Fact]
    public void Confine_IntermediateJunctionOutOfTree_Throws()
    {
        // Arrange
        var outside = NewTempDirectory("outside");
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "top secret");
        var junction = Path.Combine(_rootDir, "innerlink");
        if (!TryCreateDirectoryLink(junction, outside))
        {
            Assert.Skip("Creating symbolic links is not permitted in this environment.");
        }

        // Act
        // Assert
        Assert.Throws<PathConfinementException>(() => _confiner.Confine("innerlink/secret.txt"));
    }

    [Fact]
    public void Confine_SymlinkedRoot_ConfinesAgainstItsRealTarget()
    {
        // Arrange
        var realRoot = NewTempDirectory("realroot");
        Directory.CreateDirectory(Path.Combine(realRoot, "sub"));
        File.WriteAllText(Path.Combine(realRoot, "sub", "file.txt"), "content");
        var linkRoot = Path.Combine(NewTempDirectory("linkparent"), "linkroot");
        if (!TryCreateDirectoryLink(linkRoot, realRoot))
        {
            Assert.Skip("Creating symbolic links is not permitted in this environment.");
        }

        var confiner = new RootConfinement(linkRoot);

        // Act
        var confined = confiner.Confine("sub/file.txt");

        // Assert
        Assert.Equal("sub/file.txt", confined.RelativePath);
        Assert.True(confined.Exists);
        Assert.StartsWith(confiner.CanonicalRoot, confined.RealPath, StringComparison.Ordinal);
    }

    [Fact]
    public void Confine_InRootSymlink_ResolvesToTargetAndIsAllowed()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_rootDir, "target.txt"), "content");
        var alias = Path.Combine(_rootDir, "alias.txt");
        if (!TryCreateFileLink(alias, Path.Combine(_rootDir, "target.txt")))
        {
            Assert.Skip("Creating symbolic links is not permitted in this environment.");
        }

        // Act
        var confined = _confiner.Confine("alias.txt");

        // Assert - the denylist and output see the resolved target, not the alias name.
        Assert.Equal("target.txt", confined.RelativePath);
        Assert.True(confined.Exists);
    }

    [Fact]
    public void Confine_SiblingRootPrefixCollision_Throws()
    {
        // Arrange - a sibling whose name shares the root's prefix must not be mistaken for being inside it.
        var sibling = _rootDir + "-secret";
        Directory.CreateDirectory(sibling);
        _cleanup.Add(sibling);

        // Act
        // Assert
        Assert.Throws<PathConfinementException>(() => _confiner.Confine(sibling));
    }

    [Fact]
    public void Constructor_NullRoot_Throws()
    {
        // Arrange
        // Act
        // Assert
        Assert.Throws<ArgumentNullException>(() => new RootConfinement(null));
    }

    [Fact]
    public void Constructor_NonexistentRoot_Throws()
    {
        // Arrange
        var missing = Path.Combine(Path.GetTempPath(), "rnmcp-missing-" + Guid.NewGuid().ToString("N"));

        // Act
        // Assert
        Assert.Throws<ArgumentException>(() => new RootConfinement(missing));
    }

    private string NewTempDirectory(string label)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"rnmcp-{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _cleanup.Add(dir);
        return dir;
    }

    private void CreateFileUnderRoot(string relativePath)
    {
        var full = Path.Combine(_rootDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, "content");
    }

    private static bool TryCreateFileLink(string link, string target)
        => TryCreateLink(() => File.CreateSymbolicLink(link, target));

    private static bool TryCreateDirectoryLink(string link, string target)
        => TryCreateLink(() => Directory.CreateSymbolicLink(link, target)) || TryCreateJunctionOnWindows(link, target);

    // A directory junction is also a reparse point but needs no elevation, so it lets the confinement
    // crux run on a stock Windows box where creating a symlink would be denied.
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

    private static bool TryCreateLink(Action create)
    {
        try
        {
            create();
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }
}