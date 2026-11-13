using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;

namespace RaccoonNinja.McpToolset.Server.GitOps.Repo;

/// <summary>
/// Resolves an AI-supplied cwd to its repo top-level via <c>git -C &lt;cwd&gt; rev-parse --show-toplevel</c>, and refuses a
/// top level from which git discovery finds another git directory than it found from cwd, since a repository's own
/// <c>core.worktree</c> can set the top level to any directory, one that another repository holds included. It also
/// refuses a git directory that git's own data says belongs to another checkout, since a <c>.git</c> file, a <c>.git</c>
/// link, or a <c>commondir</c> file can name any directory. Uses an in-process cache keyed by the normalized cwd
/// (case-folded on Windows), filled only once both checks pass.
/// </summary>
/// <param name="gitExecutable">The absolute path of git, or <c>null</c> when none was found on <c>PATH</c>.</param>
public sealed class RepoRootResolver(string gitExecutable) : IRepoRootResolver
{
    private const string WorkTreeElsewhereMessage = "the work tree git reports for cwd leads to another repository or to none";
    private const string GitDirElsewhereMessage = "the git directory git reads for cwd belongs to another checkout";
    private const string GitDirectoryName = ".git";
    private const string WorktreesDirectory = "worktrees";
    private const string BacklinkFileName = "gitdir";

    /// <summary>The largest backlink file read; git writes one path there.</summary>
    private const int BacklinkMaxBytes = 4096;

    private static readonly TimeSpan BootstrapTimeout = TimeSpan.FromSeconds(10);
    private static readonly string[] ShowToplevelArgs = ["rev-parse", "--show-toplevel"];
    private static readonly string[] AbsoluteGitDirArgs = ["rev-parse", "--absolute-git-dir"];
    private static readonly string[] CommonDirArgs = ["rev-parse", "--path-format=absolute", "--git-common-dir"];
    private static readonly string[] CoreWorktreeArgs = ["config", "--local", "--get", "core.worktree"];

    private static readonly StringComparison PathComparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);

    public void ResetCache() => _cache.Clear();

    public async Task<RepoRootResolution> ResolveAsync(string cwd, CancellationToken cancellationToken = default)
    {
        ValidateCwd(cwd);

        var key = NormalizeKey(cwd);
        if (_cache.TryGetValue(key, out var cached))
            return new RepoRootResolution(cached, true);

        var root = await RunShowToplevelAsync(cwd, cancellationToken);
        var gitDir = await RequireRootFindsTheGitDirAsync(cwd, root, cancellationToken);
        await RequireGitDirBelongsToTheCheckoutAsync(root, gitDir, cancellationToken);
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
    /// <param name="args">The git arguments.</param>
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
    /// <returns>The git directory git found, without git's trailing newline.</returns>
    /// <exception cref="NotAGitRepositoryException">
    /// git failed in either directory, printed no fully qualified git directory from cwd, or found another git directory
    /// from the top level.
    /// </exception>
    private async Task<string> RequireRootFindsTheGitDirAsync(string cwd, string root, CancellationToken cancellationToken)
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

        return cwdGitDir.TrimEnd('\r', '\n');
    }

    /// <summary>
    /// Check that the git directory git reads for <paramref name="root"/> belongs to that checkout. git writes three
    /// layouts that reach a git directory away from <c>&lt;root&gt;/.git</c>: a linked worktree, whose directory under
    /// the common directory's <c>worktrees/</c> holds a <c>gitdir</c> file naming the checkout's <c>.git</c> back; an
    /// absorbed submodule, whose git directory's config sets <c>core.worktree</c>; and a separate git directory
    /// (<c>git init --separate-git-dir</c>), which names nothing back. A <c>.git</c> file, a <c>.git</c> link, or a
    /// <c>commondir</c> file can name any directory, so the check accepts: a real <c>&lt;root&gt;/.git</c> directory with
    /// no common directory of its own; a <c>worktrees/&lt;id&gt;</c> directory whose backlink names
    /// <c>&lt;root&gt;/.git</c>; a git directory whose config sets <c>core.worktree</c>; and otherwise a git directory
    /// that git would not treat as another checkout's, which a path segment named <c>.git</c> or a <c>.git</c> beside
    /// it shows. A separate git directory of another checkout, named by path, still passes.
    /// </summary>
    /// <param name="root">The top level git reported.</param>
    /// <param name="gitDir">The git directory git found for it, as a real path.</param>
    /// <param name="cancellationToken">Token to cancel the call.</param>
    /// <exception cref="NotAGitRepositoryException">The git directory belongs to another checkout, or git failed.</exception>
    private async Task RequireGitDirBelongsToTheCheckoutAsync(string root, string gitDir, CancellationToken cancellationToken)
    {
        var (commonDirOutput, commonDirExitCode) = await RunRevParseAsync(root, CommonDirArgs, cancellationToken);
        var commonDir = commonDirOutput.TrimEnd('\r', '\n');
        if (commonDirExitCode != 0 || !Path.IsPathFullyQualified(commonDir))
        {
            throw new NotAGitRepositoryException(GitDirElsewhereMessage);
        }

        var dotGit = Path.Combine(root, GitDirectoryName);
        bool belongs;
        if (SamePath(gitDir, dotGit))
        {
            belongs = SamePath(gitDir, commonDir);
        }
        else if (!SamePath(gitDir, commonDir))
        {
            belongs = SamePath(Path.GetDirectoryName(Path.GetFullPath(gitDir)), Path.Combine(commonDir, WorktreesDirectory))
                && BacklinkNamesTheCheckout(gitDir, dotGit);
        }
        else
        {
            belongs = await SetsCoreWorktreeAsync(root, cancellationToken)
                || !NamesAnotherCheckoutsGitDirectory(gitDir, dotGit);
        }

        if (!belongs)
        {
            throw new NotAGitRepositoryException(GitDirElsewhereMessage);
        }
    }

    /// <summary>Whether the git directory git reads for <paramref name="root"/> sets <c>core.worktree</c> in its own config.</summary>
    /// <param name="root">The top level git reported.</param>
    /// <param name="cancellationToken">Token to cancel the call.</param>
    /// <returns><c>true</c> when <c>git config --local --get core.worktree</c> exits 0.</returns>
    /// <exception cref="NotAGitRepositoryException">git did not exit within the timeout.</exception>
    private async Task<bool> SetsCoreWorktreeAsync(string root, CancellationToken cancellationToken)
    {
        var (_, exitCode) = await RunRevParseAsync(root, CoreWorktreeArgs, cancellationToken);
        return exitCode == 0;
    }

    /// <summary>
    /// Whether the <c>gitdir</c> file in the linked worktree directory <paramref name="gitDir"/> names
    /// <paramref name="dotGit"/>. git writes that file as one path, absolute or relative to <paramref name="gitDir"/>,
    /// so a missing file, a link, an empty file, or one over <see cref="BacklinkMaxBytes"/> names nothing; checking that
    /// before the read also keeps a FIFO, which an open would wait on, from being opened.
    /// </summary>
    /// <param name="gitDir">The linked worktree's git directory.</param>
    /// <param name="dotGit">The checkout's <c>.git</c> path.</param>
    /// <returns><c>true</c> when the file names <paramref name="dotGit"/>.</returns>
    private static bool BacklinkNamesTheCheckout(string gitDir, string dotGit)
    {
        var backlink = new FileInfo(Path.Combine(gitDir, BacklinkFileName));
        if (!backlink.Exists || backlink.LinkTarget is not null || backlink.Length is 0 or > BacklinkMaxBytes)
        {
            return false;
        }

        var line = File.ReadAllText(backlink.FullName).Split('\n')[0].Trim();
        return line.Length > 0 && SamePath(Path.GetFullPath(line, gitDir), dotGit);
    }

    /// <summary>
    /// Whether <paramref name="gitDir"/> is a git directory git treats as another checkout's: a path segment named
    /// <c>.git</c> (a checkout's own git directory, or a submodule's under it), or a <c>.git</c> next to it other than
    /// <paramref name="dotGit"/>, which the file system resolves, so another spelling of <c>.git</c> in the last segment
    /// is caught too.
    /// </summary>
    /// <param name="gitDir">The git directory, as a real path.</param>
    /// <param name="dotGit">The checkout's <c>.git</c> path.</param>
    /// <returns><c>true</c> when the git directory belongs to another checkout.</returns>
    private static bool NamesAnotherCheckoutsGitDirectory(string gitDir, string dotGit)
    {
        var full = Path.GetFullPath(gitDir);
        if (full.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => string.Equals(segment, GitDirectoryName, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var sibling = Path.Combine(Path.GetDirectoryName(full), GitDirectoryName);
        return (File.Exists(sibling) || Directory.Exists(sibling)) && !SamePath(sibling, dotGit);
    }

    /// <summary>Whether two paths name the same location, compared case-insensitively on Windows.</summary>
    /// <param name="first">One path, absolute.</param>
    /// <param name="second">The other path, absolute.</param>
    /// <returns><c>true</c> when the normalized paths are equal.</returns>
    private static bool SamePath(string first, string second)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
            PathComparison);
}