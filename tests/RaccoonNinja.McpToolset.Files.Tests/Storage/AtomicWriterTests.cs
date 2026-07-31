using System.Security.AccessControl;
using System.Security.Principal;
using RaccoonNinja.McpToolset.Files.Storage;

namespace RaccoonNinja.McpToolset.Files.Tests.Storage;

public sealed class AtomicWriterTests : IDisposable
{
    private readonly string _dir;

    public AtomicWriterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "rnmcp-atomic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
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

    [Fact]
    public void WriteNew_NewDestination_WritesBytesAndReportsWritten()
    {
        // Arrange
        var destination = Path.Combine(_dir, "sub", "file.txt");
        var content = "hello atomic"u8.ToArray();

        // Act
        var outcome = AtomicWriter.WriteNew(destination, content);

        // Assert - creates the parent directory and the file.
        Assert.Equal(AtomicWriteOutcome.Written, outcome);
        Assert.Equal(content, File.ReadAllBytes(destination));
    }

    [Fact]
    public void WriteNew_Success_LeavesNoTempFileBehind()
    {
        // Arrange
        var destination = Path.Combine(_dir, "file.txt");

        // Act
        AtomicWriter.WriteNew(destination, "content"u8.ToArray());

        // Assert
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public void WriteNew_ExistingDestination_KeepsOriginalAndReportsSkipped()
    {
        // Arrange
        var destination = Path.Combine(_dir, "file.txt");
        var original = "first writer wins"u8.ToArray();
        AtomicWriter.WriteNew(destination, original);

        // Act
        var outcome = AtomicWriter.WriteNew(destination, "second writer loses"u8.ToArray());

        // Assert
        Assert.Equal(AtomicWriteOutcome.Skipped, outcome);
        Assert.Equal(original, File.ReadAllBytes(destination));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void Replace_ExistingDestination_OverwritesContent()
    {
        // Arrange
        var destination = Path.Combine(_dir, "file.txt");
        AtomicWriter.WriteNew(destination, "old"u8.ToArray());
        var updated = "new content"u8.ToArray();

        // Act
        AtomicWriter.Replace(destination, updated);

        // Assert
        Assert.Equal(updated, File.ReadAllBytes(destination));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void Replace_MissingDestination_CreatesIt()
    {
        // Arrange
        var destination = Path.Combine(_dir, "nested", "file.txt");
        var content = "fresh"u8.ToArray();

        // Act
        AtomicWriter.Replace(destination, content);

        // Assert
        Assert.Equal(content, File.ReadAllBytes(destination));
    }

    [Fact]
    public void WriteNew_EmptyContent_WritesEmptyFile()
    {
        // Arrange
        var destination = Path.Combine(_dir, "empty.txt");

        // Act
        AtomicWriter.WriteNew(destination, []);

        // Assert
        Assert.True(File.Exists(destination));
        Assert.Empty(File.ReadAllBytes(destination));
    }

    [Fact]
    public void WriteNew_NullContent_Throws()
    {
        // Arrange
        var destination = Path.Combine(_dir, "file.txt");

        // Act
        // Assert
        Assert.Throws<ArgumentNullException>(() => AtomicWriter.WriteNew(destination, null));
    }

    [Fact]
    public void WriteNew_BlankDestination_Throws()
    {
        // Arrange
        // Act
        // Assert
        Assert.Throws<ArgumentException>(() => AtomicWriter.WriteNew("   ", "x"u8.ToArray()));
    }

    [Fact]
    public void ReplacePreservingMetadata_ExistingDestination_OverwritesAndReportsPreserved()
    {
        // Arrange
        var destination = Path.Combine(_dir, "file.txt");
        AtomicWriter.WriteNew(destination, "old"u8.ToArray());
        var updated = "new content"u8.ToArray();

        // Act
        var preserved = AtomicWriter.ReplacePreservingMetadata(destination, updated);

        // Assert
        Assert.True(preserved);
        Assert.Equal(updated, File.ReadAllBytes(destination));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void ReplacePreservingMetadata_MissingDestination_CreatesItAndReportsPreserved()
    {
        // Arrange
        var destination = Path.Combine(_dir, "nested", "file.txt");
        var content = "fresh"u8.ToArray();

        // Act
        var preserved = AtomicWriter.ReplacePreservingMetadata(destination, content);

        // Assert
        Assert.True(preserved);
        Assert.Equal(content, File.ReadAllBytes(destination));
    }

    [Fact]
    public void ReplacePreservingMetadata_UnixMode_PreservesModeStrippingElevatedBits()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes do not apply on Windows.");
        }
        else
        {
            // Arrange
            const UnixFileMode seeded = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.SetUser | UnixFileMode.SetGroup | UnixFileMode.StickyBit;
            var destination = Path.Combine(_dir, "mode.txt");
            File.WriteAllBytes(destination, "old"u8.ToArray());
            File.SetUnixFileMode(destination, seeded);

            // Act
            var preserved = AtomicWriter.ReplacePreservingMetadata(destination, "new"u8.ToArray());

            // Assert
            const UnixFileMode elevated = UnixFileMode.SetUser | UnixFileMode.SetGroup | UnixFileMode.StickyBit;
            const UnixFileMode expected = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead;
            var actual = File.GetUnixFileMode(destination);
            Assert.True(preserved);
            Assert.Equal(UnixFileMode.None, actual & elevated);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void ReplacePreservingMetadata_WindowsAcl_PreservesTheDiscretionaryAcl()
    {
        if (OperatingSystem.IsWindows())
        {
            // Arrange
            var destination = Path.Combine(_dir, "acl.txt");
            File.WriteAllBytes(destination, "old"u8.ToArray());
            var seeded = new FileInfo(destination);
            var security = seeded.GetAccessControl(AccessControlSections.Access);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: true);
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                FileSystemRights.Read,
                AccessControlType.Allow));
            seeded.SetAccessControl(security);
            var expectedSddl = new FileInfo(destination)
                .GetAccessControl(AccessControlSections.Access)
                .GetSecurityDescriptorSddlForm(AccessControlSections.Access);

            // Act
            var preserved = AtomicWriter.ReplacePreservingMetadata(destination, "new"u8.ToArray());

            // Assert
            var actualSddl = new FileInfo(destination)
                .GetAccessControl(AccessControlSections.Access)
                .GetSecurityDescriptorSddlForm(AccessControlSections.Access);
            Assert.True(preserved);
            Assert.Equal(expectedSddl, actualSddl);
            Assert.Equal("new"u8.ToArray(), File.ReadAllBytes(destination));
        }
        else
        {
            Assert.Skip("File ACLs are a Windows concept.");
        }
    }
}