using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;

namespace RaccoonNinja.McpToolset.Server.GitOps.Repo;

/// <summary>
/// Resolves an AI-supplied cwd to its repo top-level via <c>git -C &lt;cwd&gt; rev-parse --show-toplevel</c>, and refuses a
/// top level from which git discovery finds another git directory than it found from cwd, since a repository's own
/// <c>core.worktree</c> can set the top level to any directory, one that another repository holds included. Uses an
/// in-process cache keyed by the normalized cwd (case-folded on Windows), filled only once the check passes.
/// </summary>
/// <param name="gitExecutable">The absolute path of git, or <c>null</c> when none was found on <c>PATH</c>.</param>
public sealed class RepoRootResolver(string gitExecutable) : IRepoRootResolver
{
    private const string WorkTreeElsewhereMessage = "the work tree git reports for cwd leads to another repository or to none";
    private static readonly TimeSpan BootstrapTimeout = TimeSpan.FromSeconds(10);
    private static readonly string[] ShowToplevelArgs = ["rev-parse", "--show-toplevel"];
    private static readonly string[] AbsoluteGitDirArgs = ["rev-parse", "--absolute-git-dir"];

    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);

    public void ResetCache() => _cache.Clear();

    public async Task<RepoRootResolution> ResolveAsync(string cwd, CancellationToken cancellationToken = default)
    {
        ValidateCwd(cwd);

        var key = NormalizeKey(cwd);
        if (_cache.TryGetValue(key, out var cached))
            return new RepoRootResolution(cached, true);

        var root = await RunShowToplevelAsync(cwd, cancellationToken);
        await RequireRootFindsTheGitDirAsync(cwd, root, cancellationToken);
        _cache.TryAdd(key, root);
        return new RepoRootResolution(root, false);
    }

    private static void ValidateCwd(string cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd))
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
        var (stdout, exitCode) = await RunRevParseAsync(cwd, ShowToplevelArgs, cancellationToken);
        if (exitCode != 0)
        {
            throw new NotAGitRepositoryException(
                "cwd is not inside a git work tree (rev-parse failed)",
                new Dictionary<string, object> { ["exit_code"] = exitCode });
        }
        var top = stdout.Trim();
        if (string.IsNullOrWhiteSpace(top))
            throw new NotAGitRepositoryException("git rev-parse returned an empty toplevel for cwd");

        var resolved = Path.GetFullPath(top);
        return !Directory.Exists(resolved)
            ? throw new NotAGitRepositoryException("resolved repo root does not exist or is not a directory")
            : resolved;
    }

    /// <summary>
    /// Run <c>git -C <paramref name="directory"/> &lt;args&gt;</c> through <see cref="BootstrapGitRunner"/>, under the
    /// bootstrap timeout.
    /// </summary>
    /// <param name="directory">The directory git runs in.</param>
    /// <param name="args">The <c>rev-parse</c> arguments.</param>
    /// <param name="cancellationToken">Token to cancel the call.</param>
    /// <returns>git's standard output and exit code.</returns>
    /// <exception cref="NotAGitRepositoryException">git did not exit within the timeout.</exception>
    /// <exception cref="GitNotInstalledException">git could not be started.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    private async Task<(string Stdout, int ExitCode)> RunRevParseAsync(
        string directory,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        try
        {
            return await BootstrapGitRunner.RunAsync(
                gitExecutable,
                directory,
                args,
                BootstrapTimeout,
                onTimeout: null,
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new NotAGitRepositoryException(
                "git rev-parse timed out for cwd; not a usable repository",
                new Dictionary<string, object> { ["cwd_hash"] = "<redacted>" });
        }
    }

    /// <summary>
    /// Check that git discovery from <paramref name="root"/> finds the git directory git found from
    /// <paramref name="cwd"/>. Each side runs <c>git rev-parse --absolute-git-dir</c>, which git prints as its own real
    /// path, so a symbolic link or a junction in cwd's spelling names the same directory on both sides, and the two
    /// outputs are compared as git printed them. git before 2.13.0 has no such option and prints the argument back, which
    /// is not a fully qualified path, so the call is refused there.
    /// </summary>
    /// <param name="cwd">The caller's directory.</param>
    /// <param name="root">The top level git reported for cwd.</param>
    /// <param name="cancellationToken">Token to cancel the call.</param>
    /// <exception cref="NotAGitRepositoryException">
    /// git failed in either directory, printed no fully qualified git directory from cwd, or found another git directory
    /// from the top level.
    /// </exception>
    private async Task RequireRootFindsTheGitDirAsync(string cwd, string root, CancellationToken cancellationToken)
    {
        var (cwdGitDir, cwdExitCode) = await RunRevParseAsync(cwd, AbsoluteGitDirArgs, cancellationToken);
        var (rootGitDir, rootExitCode) = await RunRevParseAsync(root, AbsoluteGitDirArgs, cancellationToken);
        if (cwdExitCode != 0
            || rootExitCode != 0
            || !Path.IsPathFullyQualified(cwdGitDir)
            || !string.Equals(cwdGitDir, rootGitDir, StringComparison.Ordinal))
        {
            throw new NotAGitRepositoryException(WorkTreeElsewhereMessage);
        }
    }
}