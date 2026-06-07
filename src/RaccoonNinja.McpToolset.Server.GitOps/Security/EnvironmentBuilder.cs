using System.Runtime.InteropServices;

namespace RaccoonNinja.McpToolset.Server.GitOps.Security;

/// <summary>
/// Builds the child-process environment from an allowlist (Layer 2 hardening).
/// Anything NOT named here is dropped from the child env; fixed git neutralizers
/// are unconditionally set.
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
        "HOME"
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
        "PATHEXT"
    ];

    private static readonly Dictionary<string, string> FixedGitEnv = new()
    {
        ["GIT_TERMINAL_PROMPT"] = "0",
        ["GIT_CONFIG_NOSYSTEM"] = "1",
        ["GIT_OPTIONAL_LOCKS"] = "0",
        ["GIT_LITERAL_PATHSPECS"] = "1",
    };

    /// <summary>Construct the hardened child env from <paramref name="parent"/> (defaults to current process env).</summary>
    public static IDictionary<string, string> Build(IDictionary<string, string> parent = null)
    {
        var keyComparer = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        var src = parent == null
            ? GetCurrentEnvironment(keyComparer)
            : new Dictionary<string, string>(parent, keyComparer);

        var env = new Dictionary<string, string>(keyComparer);
        foreach (var key in BaseAllowlist)
        {
            if (src.TryGetValue(key, out var value) && value != null)
                env[key] = value;
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            foreach (var key in WindowsExtras)
            {
                if (src.TryGetValue(key, out var value) && value != null)
                    env[key] = value;
            }
        }
        foreach (var kvp in FixedGitEnv) env[kvp.Key] = kvp.Value;
        return env;
    }

    private static Dictionary<string, string> GetCurrentEnvironment(StringComparer comparer)
    {
        var result = new Dictionary<string, string>(comparer);
        foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key)
            {
                result[key] = entry.Value as string ?? string.Empty;
            }
        }
        return result;
    }
}