using System.Diagnostics;
using RaccoonNinja.McpToolset.Server.FileVault.Configuration;
using RaccoonNinja.McpToolset.Server.FileVault.Domain;
using RaccoonNinja.McpToolset.Server.FileVault.Errors;
using RaccoonNinja.McpToolset.Server.FileVault.Security;

namespace RaccoonNinja.McpToolset.Server.FileVault.Services;

/// <summary>
/// Project resolution. <c>project</c> namespaces files; the same name may exist under different
/// projects. Resolution is deterministic and, when it cannot infer a project, throws
/// <c>ambiguous_project</c> rather than guessing. Priority: explicit argument →
/// <c>VAULT_MCP_PROJECT</c> → derived from the working directory (with monorepo detection).
/// </summary>
public sealed class ProjectResolver(VaultConfig config, string cwd)
{
    private const int GitTimeoutMs = 3_000;

    /// <summary>
    /// The cwd derivation is pure for the process lifetime (the cwd never changes), so it is
    /// computed once on first use — without this, every tool call that omits `project` would
    /// re-probe the filesystem and re-spawn the git subprocess.
    /// </summary>
    private readonly Lazy<string> _derivedProject = new(() => DeriveFromCwd(cwd));

    /// <summary>Workspace markers that indicate a monorepo root, so a sub-app cwd resolves to <c>&lt;repo&gt;/&lt;app&gt;</c>.</summary>
    private static readonly string[] WorkspaceMarkers =
    [
        "pnpm-workspace.yaml",
        "nx.json",
        "turbo.json",
        "lerna.json",
        "go.work",
    ];

    /// <summary>The working directory captured at server startup.</summary>
    public string Cwd { get; } = cwd;

    /// <summary>Resolve a project name in priority order.</summary>
    /// <param name="explicitProject">The caller-supplied project, if any.</param>
    /// <returns>The validated project.</returns>
    /// <exception cref="VaultException">
    /// Thrown with <see cref="VaultErrorCode.InvalidName"/> when an explicit/env value is
    /// malformed, or <see cref="VaultErrorCode.AmbiguousProject"/> when no valid project can be
    /// inferred.
    /// </exception>
    public ProjectName Resolve(string explicitProject)
    {
        var fromArg = explicitProject?.Trim();
        if (!string.IsNullOrWhiteSpace(fromArg))
        {
            return ProjectName.Parse(fromArg);
        }

        var fromEnv = config.ProjectOverride?.Trim();
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return ProjectName.Parse(fromEnv);
        }

        var tried = new List<string>();
        var derived = _derivedProject.Value;
        if (derived is not null)
        {
            try
            {
                return ProjectName.Parse(derived);
            }
            catch (VaultException)
            {
                tried.Add(derived);
            }
        }

        throw VaultException.AmbiguousProject(tried);
    }

    /// <summary>
    /// Derive a project string from the working directory: <c>&lt;repo&gt;/&lt;app&gt;</c> when
    /// the cwd sits beneath an enclosing root, else the bare folder name. The enclosing root is
    /// the nearest ancestor carrying a workspace marker, falling back to the surrounding git
    /// repository (a coarser, external signal, so it is only consulted second).
    /// </summary>
    private static string DeriveFromCwd(string cwd)
    {
        var app = SafeFileName(cwd);
        if (app is null)
        {
            return null;
        }

        var workspaceRoot = FindWorkspaceRoot(cwd);
        if (workspaceRoot is not null)
        {
            return NamespaceFromRoot(cwd, workspaceRoot, app);
        }

        var gitRoot = GitTopLevel(cwd);
        return gitRoot is not null ? NamespaceFromRoot(cwd, gitRoot, app) : app;
    }

    /// <summary>
    /// Compose the namespace for <paramref name="cwd"/> given an enclosing <paramref name="root"/>:
    /// <c>&lt;root-name&gt;/&lt;app&gt;</c> when the root is a parent of the cwd, or just the app
    /// name when the cwd is itself the root. Both sides are compared fully-resolved, so the check
    /// is robust to path-style differences (git emits forward slashes on Windows).
    /// </summary>
    private static string NamespaceFromRoot(string cwd, string root, string app)
    {
        if (!SameDirectory(cwd, root))
        {
            var repo = SafeFileName(root);
            if (repo is not null)
            {
                return $"{repo}/{app}";
            }
        }

        return app;
    }

    private static bool SameDirectory(string a, string b)
    {
        var left = Path.TrimEndingDirectorySeparator(Path.GetFullPath(a));
        var right = Path.TrimEndingDirectorySeparator(Path.GetFullPath(b));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(left, right, comparison);
    }

    private static string SafeFileName(string path)
    {
        try
        {
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Walk from <paramref name="start"/> up to the filesystem root, returning the nearest ancestor carrying a workspace marker.</summary>
    private static string FindWorkspaceRoot(string start)
    {
        var directory = start;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (HasWorkspaceMarker(directory))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }

    private static bool HasWorkspaceMarker(string directory)
    {
        if (WorkspaceMarkers.Any(marker => File.Exists(Path.Combine(directory, marker))))
        {
            return true;
        }

        // Cargo.toml and pyproject.toml live in every single-package project, so they only mark
        // a monorepo root when they carry an explicit workspace table.
        var cargo = ReadFileOrNull(Path.Combine(directory, "Cargo.toml"));
        if (cargo is not null && cargo.Contains("[workspace]", StringComparison.Ordinal))
        {
            return true;
        }

        var pyproject = ReadFileOrNull(Path.Combine(directory, "pyproject.toml"));
        return pyproject is not null
            && (pyproject.Contains("[tool.uv.workspace]", StringComparison.Ordinal)
                || pyproject.Contains("[tool.rye.workspace]", StringComparison.Ordinal));
    }

    private static string ReadFileOrNull(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Best-effort <c>git rev-parse --show-toplevel</c> run from <paramref name="cwd"/>. Returns
    /// <c>null</c> if git is not installed, the cwd is not inside a work tree, or the command
    /// fails or times out — every such case falls back to the directory name, so git is a bonus
    /// signal, never a hard dependency. The child runs with the hardened allowlist environment
    /// and a kill-on-timeout guard.
    /// </summary>
    private static string GitTopLevel(string cwd)
    {
        // Resolve git from PATH explicitly so the CreateProcess search order can never pick
        // up a git binary planted in the (untrusted) working directory.
        var gitPath = ResolveGitFromPath();
        if (gitPath is null)
        {
            return null;
        }

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = gitPath,
                WorkingDirectory = cwd,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            process.StartInfo.ArgumentList.Add("rev-parse");
            process.StartInfo.ArgumentList.Add("--show-toplevel");
            process.StartInfo.Environment.Clear();
            foreach (var (key, value) in EnvironmentBuilder.Build())
            {
                process.StartInfo.Environment[key] = value;
            }

            if (!process.Start())
            {
                return null;
            }

            // Both pipes are drained asynchronously: a synchronous stdout read would defeat the
            // timeout when git wedges, and an undrained stderr pipe can deadlock a chatty child.
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(GitTimeoutMs))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            // The parameterless overload waits for the redirected streams to finish flushing.
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                return null;
            }

            var top = stdoutTask.GetAwaiter().GetResult().Trim();
            return top.Length == 0 ? null : top;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    /// <summary>Locate the git executable by scanning PATH directories only (never the cwd).</summary>
    private static string ResolveGitFromPath()
    {
        var fileName = OperatingSystem.IsWindows() ? "git.exe" : "git";
        var searchPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in searchPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry is skipped, matching how the OS loader treats it.
            }
        }

        return null;
    }
}