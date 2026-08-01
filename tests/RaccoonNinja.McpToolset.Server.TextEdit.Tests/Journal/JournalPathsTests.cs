using RaccoonNinja.McpToolset.Files.Security;
using RaccoonNinja.McpToolset.Server.TextEdit.Configuration;
using RaccoonNinja.McpToolset.Server.TextEdit.Journal;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Tests.Journal;

public sealed class JournalPathsTests : IDisposable
{
    private readonly List<string> _temps = [];

    public void Dispose()
    {
        foreach (var dir in _temps)
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
    public void Resolve_SameRoot_ProducesSameHashAndDirectory()
    {
        // Arrange
        var root = NewTempDir();
        var appData = NewTempDir();
        var confinement = new RootConfinement(root);

        // Act
        var first = JournalPaths.Resolve(confinement, appData);
        var second = JournalPaths.Resolve(confinement, appData);

        // Assert
        Assert.Equal(first.RootHash, second.RootHash);
        Assert.Equal(first.Dir, second.Dir);
    }

    [Fact]
    public void Resolve_DistinctRoots_ProduceDistinctHashes()
    {
        // Arrange
        var appData = NewTempDir();
        var firstRoot = new RootConfinement(NewTempDir());
        var secondRoot = new RootConfinement(NewTempDir());

        // Act
        var first = JournalPaths.Resolve(firstRoot, appData);
        var second = JournalPaths.Resolve(secondRoot, appData);

        // Assert
        Assert.NotEqual(first.RootHash, second.RootHash);
    }

    [Fact]
    public void Resolve_CreatesDirectoryAndBlobStore()
    {
        // Arrange
        var confinement = new RootConfinement(NewTempDir());
        var appData = NewTempDir();

        // Act
        var paths = JournalPaths.Resolve(confinement, appData);

        // Assert
        Assert.True(Directory.Exists(paths.Dir));
        Assert.True(Directory.Exists(paths.BlobsDir));
    }

    [Fact]
    public void Resolve_JournalDirectoryInsideRoot_Throws()
    {
        // Arrange
        var root = NewTempDir();
        var insideRoot = Path.Combine(root, "journal");
        var confinement = new RootConfinement(root);

        // Act
        // Assert
        Assert.Throws<EditStartupException>(() => JournalPaths.Resolve(confinement, insideRoot));
    }

    [Fact]
    public void Resolve_CaseVariantRoot_CollapsesToOneJournalOnWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            // Arrange
            var root = NewTempDir();
            var appData = NewTempDir();
            var toggledDrive = char.ToLowerInvariant(root[0]) + root[1..];

            // Act
            var canonical = JournalPaths.Resolve(new RootConfinement(root), appData);
            var variant = JournalPaths.Resolve(new RootConfinement(toggledDrive), appData);

            // Assert
            Assert.Equal(canonical.RootHash, variant.RootHash);
        }
        else
        {
            Assert.Skip("Case-insensitive drive-letter collapse is a Windows path behavior.");
        }
    }

    [Fact]
    public void Resolve_CaseDistinctRoots_StayDistinctOnLinux()
    {
        if (OperatingSystem.IsLinux())
        {
            // Arrange
            var parent = NewTempDir();
            var appData = NewTempDir();
            var upper = Path.Combine(parent, "Repo");
            var lower = Path.Combine(parent, "repo");
            Directory.CreateDirectory(upper);
            Directory.CreateDirectory(lower);

            // Act
            var upperJournal = JournalPaths.Resolve(new RootConfinement(upper), appData);
            var lowerJournal = JournalPaths.Resolve(new RootConfinement(lower), appData);

            // Assert
            Assert.NotEqual(upperJournal.RootHash, lowerJournal.RootHash);
        }
        else
        {
            Assert.Skip("Case-distinct sibling directories only coexist on a case-sensitive file system.");
        }
    }

    private string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rnmcp-te-jp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _temps.Add(dir);
        return dir;
    }
}