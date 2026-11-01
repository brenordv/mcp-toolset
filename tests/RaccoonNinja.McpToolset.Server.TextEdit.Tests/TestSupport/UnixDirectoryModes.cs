namespace RaccoonNinja.McpToolset.Server.TextEdit.Tests.TestSupport;

/// <summary>
/// Makes a directory unlistable and listable again through its Unix mode bits, for tests of a directory the code under
/// test can enter but not list. A test that needs them calls <see cref="SkipUnlessApplicable"/> first, which skips it on
/// Windows and in a privileged process.
/// </summary>
internal static class UnixDirectoryModes
{
    /// <summary>Skip the calling test on Windows and in a privileged process, where the mode bits cannot make a directory unlistable.</summary>
    public static void SkipUnlessApplicable()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes do not apply on Windows.");
        }

        if (Environment.IsPrivilegedProcess)
        {
            Assert.Skip("A privileged process bypasses directory mode bits.");
        }
    }

    /// <summary>Leave <paramref name="directory"/> writable and searchable by its owner but not readable. Does nothing on Windows.</summary>
    /// <param name="directory">The directory's absolute path.</param>
    public static void MakeUnlistable(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(directory, UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>Give the owner read, write, and search on <paramref name="directory"/> again, so cleanup can delete it. Does nothing on Windows.</summary>
    /// <param name="directory">The directory's absolute path.</param>
    public static void MakeListable(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}