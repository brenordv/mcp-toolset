namespace RaccoonNinja.McpToolset.Server.FileVault.Security;

/// <summary>
/// Builds the child-process environment for the project-inference git shell-out from an
/// allowlist (mirroring the GitOps server). Anything not named here is dropped;
/// fixed git neutralizers are unconditionally set so inherited <c>GIT_DIR</c>/<c>GIT_CONFIG_*</c>
/// cannot redirect the probe.
/// </summary>
public static class EnvironmentBuilder
{
    private static readonly string[] BaseAllowlist =
    [
        "PATH",
        "LANG",
        "LC_ALL",
        "LC_CTYPE",
        "LC_MESSAGES",
        "LC_TIME",
        "LC_NUMERIC",
        "LC_COLLATE",
        "HOME",
    ];

    private static readonly string[] WindowsExtras =
    [
        "SystemRoot",
        "SystemDrive",
        "TEMP",
        "TMP",
        "USERPROFILE",
        "APPDATA",
        "LOCALAPPDATA",
        "PATHEXT",
    ];

    private static readonly Dictionary<string, string> FixedGitEnv = new()
    {
        ["GIT_TERMINAL_PROMPT"] = "0",
        ["GIT_CONFIG_NOSYSTEM"] = "1",
        ["GIT_OPTIONAL_LOCKS"] = "0",
    };

    /// <summary>Construct the hardened child env from the current process environment.</summary>
    /// <returns>The allowlisted environment map.</returns>
    public static IDictionary<string, string> Build()
    {
        var keyComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        var source = new Dictionary<string, string>(keyComparer);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key)
            {
                source[key] = entry.Value as string ?? string.Empty;
            }
        }

        var env = new Dictionary<string, string>(keyComparer);
        foreach (var key in BaseAllowlist)
        {
            if (source.TryGetValue(key, out var value) && value is not null)
            {
                env[key] = value;
            }
        }

        if (OperatingSystem.IsWindows())
        {
            foreach (var key in WindowsExtras)
            {
                if (source.TryGetValue(key, out var value) && value is not null)
                {
                    env[key] = value;
                }
            }
        }

        foreach (var (key, value) in FixedGitEnv)
        {
            env[key] = value;
        }

        return env;
    }
}