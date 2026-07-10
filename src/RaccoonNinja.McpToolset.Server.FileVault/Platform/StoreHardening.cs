namespace RaccoonNinja.McpToolset.Server.FileVault.Platform;

/// <summary>
/// Platform-specific hardening: restrictive store permissions and a best-effort network-drive
/// warning. SQLite's cross-process WAL locking is unreliable on network volumes, so the server
/// warns but does not refuse when it suspects the store is not on a local disk.
/// </summary>
public static class StoreHardening
{
    /// <summary>
    /// Restrict the store root to the current user. On Unix this sets mode 0700; on Windows,
    /// NTFS ACLs already default to per-user profile directories, so this is a no-op (parity
    /// with the Rust server's documented posture).
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

    /// <summary>
    /// Returns <c>true</c> when <paramref name="path"/> looks like it lives on a network or
    /// synced volume. This is a heuristic (Windows UNC prefixes only), not a guarantee.
    /// </summary>
    /// <param name="path">The store root directory.</param>
    /// <returns><c>true</c> when the path looks non-local.</returns>
    public static bool LooksLikeNetworkPath(string path)
        => path is not null && path.StartsWith(@"\\", StringComparison.Ordinal);
}