using System.Globalization;
using RaccoonNinja.McpToolset.Server.FileVault.Exceptions;
using RaccoonNinja.McpToolset.Server.FileVault.Platform;

namespace RaccoonNinja.McpToolset.Server.FileVault.Configuration;

/// <summary>
/// Resolved runtime configuration for one server process. The store is a single global directory,
/// defaulting to <c>~/.vault-mcp</c> and overridable via <c>VAULT_MCP_HOME</c>.
/// </summary>
public sealed record VaultConfig
{
    /// <summary>Default per-call content size limit: 10 MiB.</summary>
    public const long DefaultMaxContentBytes = 10L * 1024 * 1024;

    /// <summary>Default SQLite busy timeout, in milliseconds.</summary>
    public const int DefaultBusyTimeoutMs = 5_000;

    /// <summary>
    /// Default split-hint threshold, in UTF-16 code units of committed content. Writes that land
    /// above this get an advisory <c>hint</c> in their result nudging the caller to split the
    /// note into a summary + child notes.
    /// </summary>
    public const int DefaultSplitHintChars = 14_000;

    /// <summary>The store root (<c>~/.vault-mcp</c> or <c>$VAULT_MCP_HOME</c>).</summary>
    public string Home { get; init; }

    /// <summary>The SQLite database file (<c>&lt;home&gt;/vault.db</c>).</summary>
    public string DbPath { get; init; }

    /// <summary>The plain-text snapshot directory (<c>&lt;home&gt;/files</c>).</summary>
    public string FilesDir { get; init; }

    /// <summary>Per-call content byte limit.</summary>
    public long MaxContentBytes { get; init; }

    /// <summary>SQLite busy timeout in milliseconds.</summary>
    public int BusyTimeoutMs { get; init; }

    /// <summary>
    /// Committed-content length (UTF-16 code units) above which write results carry an advisory
    /// split hint. <c>0</c> disables the hint. Advisory only — never rejects a write.
    /// </summary>
    public int SplitHintChars { get; init; }

    /// <summary>An explicit project namespace from <c>VAULT_MCP_PROJECT</c>, if set (trimmed, never empty).</summary>
    public string ProjectOverride { get; init; }

    /// <summary>
    /// How purge treats the on-disk snapshots. When <c>true</c> they are deleted outright; when
    /// <c>false</c> (default) each is renamed with a <c>DELETED_</c> prefix and kept for manual
    /// recovery.
    /// </summary>
    public bool PurgeDeleteFiles { get; init; }

    /// <summary>Resolve configuration from the environment.</summary>
    /// <returns>The resolved configuration.</returns>
    /// <exception cref="VaultStartupException">
    /// Thrown when a numeric or boolean override is present but unparseable, or no home directory
    /// can be determined.
    /// </exception>
    public static VaultConfig Load()
        => WithHome(ResolveHome());

    /// <summary>Build a configuration rooted at an explicit home, applying env overrides.</summary>
    /// <param name="home">The store root directory.</param>
    /// <returns>The resolved configuration.</returns>
    /// <exception cref="VaultStartupException">Thrown when an override env var is present but invalid.</exception>
    public static VaultConfig WithHome(string home)
        => new()
        {
            Home = home,
            DbPath = Path.Combine(home, "vault.db"),
            FilesDir = Path.Combine(home, "files"),
            MaxContentBytes = ParseEnvLong("VAULT_MCP_MAX_BYTES", DefaultMaxContentBytes),
            BusyTimeoutMs = ParseEnvInt("VAULT_MCP_BUSY_TIMEOUT_MS", DefaultBusyTimeoutMs, "millisecond count"),
            SplitHintChars = ParseEnvInt("VAULT_MCP_SPLIT_HINT_CHARS", DefaultSplitHintChars, "character count"),
            ProjectOverride = ReadNonEmpty("VAULT_MCP_PROJECT"),
            PurgeDeleteFiles = ParseEnvBool("VAULT_MCP_PURGE_DELETE_FILES", false),
        };

    /// <summary>
    /// Create the store directory tree (<see cref="Home"/> and <see cref="FilesDir"/>) if missing,
    /// then restrict permissions on the home directory (0700 on Unix).
    /// </summary>
    public void EnsureDirs()
    {
        Directory.CreateDirectory(FilesDir);
        StoreHardening.RestrictPermissions(Home);
    }

    private static string ResolveHome()
    {
        var explicitHome = Environment.GetEnvironmentVariable("VAULT_MCP_HOME");
        if (!string.IsNullOrWhiteSpace(explicitHome))
        {
            return explicitHome;
        }

        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(userHome)
            ? throw new VaultStartupException("could not determine the user home directory; set VAULT_MCP_HOME")
            : Path.Combine(userHome, ".vault-mcp");
    }

    private static string ReadNonEmpty(string key)
    {
        var value = Environment.GetEnvironmentVariable(key);
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static long ParseEnvLong(string key, long defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
            ? parsed
            : throw new VaultStartupException($"{key}='{raw}' is not a valid byte count");
    }

    private static int ParseEnvInt(string key, int defaultValue, string unit)
    {
        var raw = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
            ? parsed
            : throw new VaultStartupException($"{key}='{raw}' is not a valid {unit}");
    }

    /// <summary>
    /// Parse a boolean env var, accepting the usual spellings (<c>true/false</c>, <c>1/0</c>,
    /// <c>yes/no</c>, <c>on/off</c>, case-insensitive). Unset or empty falls back to the default.
    /// </summary>
    private static bool ParseEnvBool(string key, bool defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(key);
        if (raw is null)
        {
            return defaultValue;
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "" => defaultValue,
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            var other => throw new VaultStartupException($"{key}='{other}' is not a valid boolean (use true or false)"),
        };
    }
}