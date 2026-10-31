using System.Runtime.InteropServices;

namespace RaccoonNinja.McpToolset.Server.GitOps.Security;

/// <summary>
/// Builds the child-process environment from an allowlist (Layer 2 hardening).
/// Anything NOT named here is dropped from the child env; <c>PATH</c> keeps only
/// its fully qualified entries; fixed git neutralizers are unconditionally set.
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

    /// <summary>
    /// git variables written into every child environment after the allowlist, so the parent environment cannot override
    /// them. <c>GIT_NO_LAZY_FETCH=1</c> stops git from fetching a missing object from a promisor remote on demand: that
    /// fetch runs the transport program a repository's own config can name (for example
    /// <c>remote.&lt;name&gt;.uploadpack</c> for a remote at a local path, <c>core.sshCommand</c> for ssh, or a remote helper).
    /// git 2.45.1 and later, and the May 2024 security releases for 2.39 to 2.44, check it before any fetch starts; 2.45.0
    /// honors it only in part, and an older git ignores it. <c>GIT_ALLOW_PROTOCOL=none</c> covers those gits: git then
    /// allows only the transports the variable lists, whatever <c>protocol.&lt;name&gt;.allow</c> in a repository's config
    /// says, and checks before it starts a transport program or a remote helper, so the fetch dies first. git reads it from
    /// 2.6.1 on. The value is <c>none</c> because git splits an empty value into one empty protocol name.
    /// </summary>
    private static readonly Dictionary<string, string> FixedGitEnv = new()
    {
        ["GIT_TERMINAL_PROMPT"] = "0",
        ["GIT_CONFIG_NOSYSTEM"] = "1",
        ["GIT_OPTIONAL_LOCKS"] = "0",
        ["GIT_NO_LAZY_FETCH"] = "1",
        ["GIT_ALLOW_PROTOCOL"] = "none",
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
        if (env.TryGetValue("PATH", out var pathVariable))
        {
            env["PATH"] = KeepFullyQualifiedEntries(pathVariable);
        }
        foreach (var kvp in FixedGitEnv) env[kvp.Key] = kvp.Value;
        return env;
    }

    /// <summary>
    /// Keep the fully qualified entries of a <c>PATH</c> value, in order. git looks up on <c>PATH</c> a program it starts
    /// by name, such as the <c>git log</c> that <c>git stash list</c> starts, once its own exec path holds no match; it
    /// reads an empty entry as its current directory (Linux and macOS), and a relative entry resolves against that
    /// directory, the repository root for a tool call. The result must not be empty: git writes its exec path and a
    /// separator in front of <c>PATH</c>, so an empty value would become a trailing empty entry, the current directory.
    /// It never is when a git process starts, since the entry holding the git the server starts is fully qualified.
    /// </summary>
    /// <param name="pathVariable">The parent's <c>PATH</c> value.</param>
    /// <returns>The fully qualified entries, joined with <see cref="Path.PathSeparator"/>.</returns>
    private static string KeepFullyQualifiedEntries(string pathVariable)
    {
        var kept = new List<string>();
        foreach (var entry in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (Path.IsPathFullyQualified(entry))
            {
                kept.Add(entry);
            }
        }

        return string.Join(Path.PathSeparator, kept);
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