using System.Runtime.InteropServices;
using RaccoonNinja.McpToolset.Server.GitOps.Extensions;

namespace RaccoonNinja.McpToolset.Server.GitOps.Security;

/// <summary>
/// Builds the child-process environment from an allowlist (Layer 2 hardening).
/// Anything NOT named here is dropped from the child env; <c>PATH</c> keeps only
/// its fully qualified entries, and is dropped when none is left; fixed git
/// neutralizers are unconditionally set.
/// </summary>
public static class EnvironmentBuilder
{
    private const string PathVariableName = "PATH";

    private static readonly string[] BaseAllowlist =
    [
        PathVariableName,
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
    /// <c>GIT_NO_REPLACE_OBJECTS=1</c> makes git ignore a repository's replace refs (<c>refs/replace/</c>), which make it
    /// read another object's content under the original id; every git from 2.6.1 on reads it, and from 2.42.0 it wins
    /// over <c>core.useReplaceRefs</c>, which <see cref="GitCommandBuilder"/> pins off for the gits before.
    /// <c>GIT_GRAFT_FILE</c> names the file git reads grafts from in place of <c>.git/info/grafts</c>, where a line gives
    /// a commit other parents; git reads none, and prints nothing, when the file does not exist. Every git from 2.6.1 on
    /// reads it.
    /// </summary>
    private static readonly Dictionary<string, string> FixedGitEnv = new()
    {
        ["GIT_TERMINAL_PROMPT"] = "0",
        ["GIT_CONFIG_NOSYSTEM"] = "1",
        ["GIT_OPTIONAL_LOCKS"] = "0",
        ["GIT_NO_LAZY_FETCH"] = "1",
        ["GIT_ALLOW_PROTOCOL"] = "none",
        ["GIT_NO_REPLACE_OBJECTS"] = "1",
        ["GIT_GRAFT_FILE"] = "/nonexistent/git-ops-grafts-disabled",
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
        if (env.TryGetValue(PathVariableName, out var pathVariable))
        {
            var fullyQualified = KeepFullyQualifiedEntries(pathVariable);
            if (fullyQualified.Length > 0)
            {
                env[PathVariableName] = fullyQualified;
            }
            else
            {
                env.Remove(PathVariableName);
            }
        }
        foreach (var kvp in FixedGitEnv) env[kvp.Key] = kvp.Value;
        return env;
    }

    /// <summary>
    /// Keep the fully qualified entries of a <c>PATH</c> value, in order. git looks up on <c>PATH</c> a program it starts
    /// by name, such as the <c>git log</c> that <c>git stash list</c> starts, once its own exec path holds no match; on
    /// Linux and macOS it reads an empty entry as its current directory, and a relative entry resolves against that
    /// directory, the repository root for a tool call. git puts its exec path and a separator in front of the <c>PATH</c>
    /// it inherits and uses a built-in default when <c>PATH</c> is unset, so <see cref="Build"/> drops an empty result
    /// instead of handing git a <c>PATH</c> that ends in an empty entry. No git the server starts meets that case: the
    /// entry holding it is fully qualified.
    /// </summary>
    /// <param name="pathVariable">The parent's <c>PATH</c> value.</param>
    /// <returns>The fully qualified entries, joined with <see cref="Path.PathSeparator"/>; empty when none is left.</returns>
    private static string KeepFullyQualifiedEntries(string pathVariable)
        => string.Join(Path.PathSeparator, pathVariable.FullyQualifiedPathEntries());

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