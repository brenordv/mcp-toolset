using System.Runtime.InteropServices;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.LogFileValidatorExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Logging;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Logging;

public class LogFileValidatorTests
{
    [Fact]
    public void Empty_Path_Is_Rejected()
    {
        Assert.Throws<LogPathRejectedException>(() => LogFileValidator.Validate(string.Empty));
    }

    [Fact]
    public void Relative_Path_Is_Rejected()
    {
        Assert.Throws<LogPathRejectedException>(() => LogFileValidator.Validate("relative.log"));
    }

    [Fact]
    public void Control_Char_Is_Rejected()
    {
        Assert.Throws<LogPathRejectedException>(() => LogFileValidator.Validate("/tmp/withctl.log"));
    }

    [Fact]
    public void UNC_Prefix_Is_Rejected()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        Assert.Throws<LogPathRejectedException>(() => LogFileValidator.Validate(@"\\server\share\foo.log"));
    }

    [Fact]
    public void Extended_Length_Prefix_Is_Rejected()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        Assert.Throws<LogPathRejectedException>(() => LogFileValidator.Validate(@"\\?\C:\foo.log"));
    }

    [Fact]
    public void Missing_Parent_Directory_Is_Rejected()
    {
        var path = Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid(), "log.txt");
        Assert.Throws<LogPathRejectedException>(() => LogFileValidator.Validate(path));
    }

    [Fact]
    public void Existing_Writable_Parent_Is_Accepted()
    {
        var parent = Path.Combine(Path.GetTempPath(), "log-parent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        try
        {
            var path = Path.Combine(parent, "ok.log");
            var resolved = LogFileValidator.Validate(path);
            Assert.Equal(Path.GetFullPath(path), resolved);
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }
}