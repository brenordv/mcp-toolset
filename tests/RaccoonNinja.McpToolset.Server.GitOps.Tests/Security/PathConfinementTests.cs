using System.Runtime.InteropServices;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Security;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Security;

public class PathConfinementTests
{
    [Fact]
    public void Confine_Returns_PosixRelative_Path_For_Subdir()
    {
        var root = Path.Combine(Path.GetTempPath(), "confine-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "src"));
        var rel = PathConfinement.Confine(root, "src/foo.cs");
        Assert.Equal("src/foo.cs", rel);
    }

    [Fact]
    public void Confine_Returns_Dot_For_Root_Itself()
    {
        var root = Path.Combine(Path.GetTempPath(), "confine-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Assert.Equal(".", PathConfinement.Confine(root, "."));
    }

    [Fact]
    public void Confine_Rejects_Path_That_Escapes_Root()
    {
        var root = Path.Combine(Path.GetTempPath(), "confine-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Assert.Throws<PathOutsideRepoException>(() => PathConfinement.Confine(root, "../escape"));
    }

    [Fact]
    public void Confine_Rejects_UNC_Path()
    {
        Assert.Throws<PathOutsideRepoException>(() => PathConfinement.Confine(@"C:\anything", @"\\server\share"));
    }

    [Fact]
    public void Confine_Rejects_Path_Starting_With_Dash()
    {
        Assert.Throws<RejectedArgumentException>(() => PathConfinement.Confine(@"C:\anything", "-evil"));
    }

    [Fact]
    public void Confine_Rejects_Alternate_Data_Stream_On_Windows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;
        Assert.Throws<PathOutsideRepoException>(() => PathConfinement.Confine(@"C:\anything", @"foo:bar"));
    }
}