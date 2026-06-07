using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;

namespace RaccoonNinja.McpToolset.Server.GitOps.Repo;

/// <summary>
/// Resolves an AI-supplied cwd to its repo top-level via
/// <c>git -C &lt;cwd&gt; rev-parse --show-toplevel</c>. Uses an in-process cache
/// keyed by the normalized cwd (case-folded on Windows).
/// </summary>
public sealed class RepoRootResolver(string gitExecutable = "git") : IRepoRootResolver
{
    private static readonly TimeSpan BootstrapTimeout = TimeSpan.FromSeconds(10);
    private static readonly string[] ShowToplevelArgs = ["rev-parse", "--show-toplevel"];

    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);

    public void ResetCache() => _cache.Clear();

    public async Task<RepoRootResolution> ResolveAsync(string cwd, CancellationToken cancellationToken = default)
    {
        ValidateCwd(cwd);

        var key = NormalizeKey(cwd);
        if (_cache.TryGetValue(key, out var cached))
            return new RepoRootResolution(cached, true);

        var root = await RunShowToplevelAsync(cwd, cancellationToken).ConfigureAwait(false);
        _cache.TryAdd(key, root);
        return new RepoRootResolution(root, false);
    }

    private static void ValidateCwd(string cwd)
    {
        if (string.IsNullOrEmpty(cwd))
            throw new RejectedArgumentException("cwd must be a non-empty absolute path string",
                new Dictionary<string, object> { ["param"] = "cwd" });
        if (cwd.Contains('\0'))
            throw new RejectedArgumentException("cwd contains NUL",
                new Dictionary<string, object> { ["param"] = "cwd" });
        if (!Path.IsPathFullyQualified(cwd))
            throw new RejectedArgumentException("cwd must be absolute",
                new Dictionary<string, object> { ["param"] = "cwd" });
        if (!Directory.Exists(cwd))
            throw new RejectedArgumentException("cwd does not exist",
                new Dictionary<string, object> { ["param"] = "cwd" });
    }

    private static string NormalizeKey(string cwd)
    {
        var norm = Path.GetFullPath(cwd);
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? norm.ToLowerInvariant()
            : norm;
    }

    private async Task<string> RunShowToplevelAsync(string cwd, CancellationToken cancellationToken)
    {
        string stdout;
        int exitCode;
        try
        {
            (stdout, exitCode) = await BootstrapGitRunner.RunAsync(
                gitExecutable,
                cwd,
                ShowToplevelArgs,
                BootstrapTimeout,
                onTimeout: null,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new NotAGitRepositoryException(
                "git rev-parse timed out for cwd; not a usable repository",
                new Dictionary<string, object> { ["cwd_hash"] = "<redacted>" });
        }

        if (exitCode != 0)
        {
            throw new NotAGitRepositoryException(
                "cwd is not inside a git work tree (rev-parse failed)",
                new Dictionary<string, object> { ["exit_code"] = exitCode });
        }
        var top = stdout.Trim();
        if (string.IsNullOrEmpty(top))
            throw new NotAGitRepositoryException("git rev-parse returned an empty toplevel for cwd");

        var resolved = Path.GetFullPath(top);
        return !Directory.Exists(resolved)
            ? throw new NotAGitRepositoryException("resolved repo root does not exist or is not a directory")
            : resolved;
    }
}