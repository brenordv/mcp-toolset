namespace RaccoonNinja.McpToolset.Common.SkillStats.Platform;

/// <summary>
/// Platform-specific hardening for the skill-usage store. The store can hold prompt-derived
/// <c>args</c> text, so the home directory is restricted to the current user.
/// </summary>
public static class StoreHardening
{
    /// <summary>
    /// Restrict the store root to the current user. On Unix this sets mode 0700; on Windows,
    /// NTFS ACLs already default to per-user profile directories, so this is a no-op.
    /// </summary>
    /// <param name="path">The store root directory.</param>
    public static void RestrictPermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}