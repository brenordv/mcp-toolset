using System.Runtime.Versioning;

namespace RaccoonNinja.McpToolset.Server.FileVault.Security;

/// <summary>
/// Finds the git that project inference starts, in the fully qualified directories of <c>PATH</c> only. An empty or
/// relative entry resolves against the server's working directory, which is the MCP client's project and can commit a
/// program called <c>git</c>.
/// </summary>
internal static class GitExecutableLocator
{
    private const string UnixFileName = "git";

    /// <summary>The only name looked for on Windows: a <c>.cmd</c> or <c>.bat</c> would start through <c>cmd.exe</c>.</summary>
    private const string WindowsFileName = "git.exe";

    private const UnixFileMode AnyExecute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    /// <summary>Return the first git in the fully qualified entries of <paramref name="pathVariable"/>, skipping empty and relative entries.</summary>
    /// <param name="pathVariable">The value of the <c>PATH</c> environment variable.</param>
    /// <returns>The full path of the first match, or <c>null</c> when no entry holds git.</returns>
    public static string Find(string pathVariable)
    {
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return null;
        }

        var fileName = OperatingSystem.IsWindows() ? WindowsFileName : UnixFileName;
        foreach (var entry in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Path.IsPathFullyQualified(entry))
            {
                continue;
            }

            var candidate = Path.Combine(entry, fileName);
            if (IsStartable(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>A file, not a directory; off Windows it also needs an execute bit.</summary>
    private static bool IsStartable(string candidate)
    {
        if (!File.Exists(candidate))
        {
            return false;
        }

        if (!OperatingSystem.IsWindows())
        {
            return HasExecuteBit(candidate);
        }

        return true;
    }

    /// <summary>Any execute bit, read through a symlink; a broken link or a file that vanished reads as not executable.</summary>
    [UnsupportedOSPlatform("windows")]
    private static bool HasExecuteBit(string candidate)
    {
        try
        {
            return (File.GetUnixFileMode(candidate) & AnyExecute) != 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}