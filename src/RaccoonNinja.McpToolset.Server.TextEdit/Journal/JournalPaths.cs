using System.Text;
using RaccoonNinja.McpToolset.Files.Security;
using RaccoonNinja.McpToolset.Server.TextEdit.Configuration;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Journal;

/// <summary>
/// Resolves and hardens the per-root journal directory in platform app-data. The directory is keyed by
/// the full-length BLAKE3 hash of the confiner's own canonical root, taken verbatim: the confiner already
/// canonicalizes case per OS (so two spellings of one Windows path collapse, while two case-distinct Linux
/// paths stay separate), and deriving the key from that same string guarantees the journal and the confiner
/// can never disagree about which root they serve. Two invariants are enforced at resolution, both fatal:
/// the journal directory must sit outside the edit root (so the server's own write tools cannot poison its
/// pre-images), and on Unix it must be lockable to the current user (or the plaintext pre-images would be
/// world-readable).
/// </summary>
public sealed class JournalPaths
{
    private const string WindowsVendorDir = "RaccoonNinja";
    private const string UnixVendorDir = "raccoonninja";
    private const string AppDir = "text-edit";
    private const string BlobsDirName = "blobs";
    private const string DbFileName = "journal.db";

    private JournalPaths(string dir, string rootHash)
    {
        Dir = dir;
        RootHash = rootHash;
    }

    /// <summary>The resolved, hardened journal directory (outside the edit root).</summary>
    public string Dir { get; }

    /// <summary>The full-length BLAKE3 hex of the canonical root that keys this journal.</summary>
    public string RootHash { get; }

    /// <summary>The SQLite database file inside the journal directory.</summary>
    public string DbPath => Path.Combine(Dir, DbFileName);

    /// <summary>The pre-image blob store directory inside the journal directory.</summary>
    public string BlobsDir => Path.Combine(Dir, BlobsDirName);

    /// <summary>Resolve the journal directory for <paramref name="root"/> under the platform app-data base.</summary>
    /// <param name="root">The confiner for the edit root.</param>
    /// <returns>The resolved, created, and hardened journal paths.</returns>
    /// <exception cref="EditStartupException">Thrown when the directory would sit inside the root, or hardening fails on Unix.</exception>
    public static JournalPaths Resolve(RootConfinement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return Resolve(root, ResolveAppDataBase());
    }

    /// <summary>Resolve the journal directory under an explicit app-data base (the env-free path, used by tests).</summary>
    /// <param name="root">The confiner for the edit root.</param>
    /// <param name="appDataBase">The base directory the per-root journal directory is created under.</param>
    /// <returns>The resolved, created, and hardened journal paths.</returns>
    /// <exception cref="EditStartupException">Thrown when the directory would sit inside the root, or hardening fails on Unix.</exception>
    internal static JournalPaths Resolve(RootConfinement root, string appDataBase)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataBase);

        var canonicalRoot = root.CanonicalRoot;
        var rootHash = Blake3.Hasher.Hash(Encoding.UTF8.GetBytes(canonicalRoot)).ToString();
        var dir = Path.GetFullPath(Path.Combine(appDataBase, rootHash));

        if (root.ContainsPath(dir))
        {
            throw new EditStartupException(
                "the journal directory resolves inside the edit root; the pre-image store must live outside "
                + "the root so this server's own write tools cannot alter it. Move the journal location (or the root) so they do not overlap");
        }

        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, BlobsDirName));
        Harden(dir);

        return new JournalPaths(dir, rootHash);
    }

    private static void Harden(string dir)
    {
        if (OperatingSystem.IsWindows())
        {
            // %LOCALAPPDATA% already inherits the per-user profile ACL; no extra hardening is applied.
            return;
        }

        try
        {
            File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fail closed: a journal that cannot be locked to the current user must not hold plaintext
            // pre-images, so refuse to start rather than proceed with a possibly world-readable store. The
            // message stays path-free: ex.Message can carry the home-based journal path.
            throw new EditStartupException("could not restrict journal directory permissions to the current user");
        }
    }

    private static string ResolveAppDataBase()
    {
        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return string.IsNullOrWhiteSpace(localAppData)
                ? throw new EditStartupException("could not determine %LOCALAPPDATA% for the journal directory")
                : Path.Combine(localAppData, WindowsVendorDir, AppDir);
        }

        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdg))
        {
            return Path.Combine(xdg, UnixVendorDir, AppDir);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(home)
            ? throw new EditStartupException("could not determine the user home directory for the journal; set XDG_DATA_HOME")
            : Path.Combine(home, ".local", "share", UnixVendorDir, AppDir);
    }
}