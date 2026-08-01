using System.Runtime.InteropServices;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.LogFileValidatorExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Logging;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Logging;

public class LogFileValidatorTests
{
    [Fact]
    public void Empty_PathIsRejected()
    {
        // Act & Assert
        Assert.Throws<LogPathRejectedException>(() => LogFileValidator.Validate(string.Empty));
    }

    [Fact]
    public void Relative_PathIsRejected()
    {
        // Act & Assert
        Assert.Throws<LogPathRejectedException>(() => LogFileValidator.Validate("relative.log"));
    }

    [Fact]
    public void Control_CharIsRejected()
    {
        // Act & Assert
        Assert.Throws<LogPathRejectedException>(() => LogFileValidator.Validate("/tmp/withctl.log"));
    }

    [Fact]
    public void UNC_PrefixIsRejected()
    {
        // Arrange
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        // Act & Assert
        Assert.Throws<LogPathRejectedException>(() => LogFileValidator.Validate(@"\\server\share\foo.log"));
    }

    [Fact]
    public void Extended_LengthPrefixIsRejected()
    {
        // Arrange
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        // Act & Assert
        Assert.Throws<LogPathRejectedException>(() => LogFileValidator.Validate(@"\\?\C:\foo.log"));
    }

    [Fact]
    public void Missing_ParentDirectoryIsRejected()
    {
        // Arrange
        var path = Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid(), "log.txt");

        // Act & Assert
        Assert.Throws<LogPathRejectedException>(() => LogFileValidator.Validate(path));
    }

    [Fact]
    public void Existing_WritableParentIsAccepted()
    {
        // Arrange
        var parent = Path.Combine(Path.GetTempPath(), "log-parent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        try
        {
            var path = Path.Combine(parent, "ok.log");

            // Act
            var resolved = LogFileValidator.Validate(path);

            // Assert
            Assert.Equal(Path.GetFullPath(path), resolved);
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }
}