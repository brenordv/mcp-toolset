using System.ComponentModel;
using System.Diagnostics;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;

/// <summary>
/// A fresh, empty temp git repository (branch <c>main</c>, local identity, no signing). A test that needs
/// a history of its own builds it with <see cref="WriteFile"/>, <see cref="WriteBytes"/>, and
/// <see cref="Run"/>; <see cref="GitRepoFixture"/> builds the shared linear history on top of it.
/// </summary>
internal sealed class TempGitRepo : IDisposable
{
    /// <summary>How long one git invocation may run before the helper kills it and fails the test.</summary>
    private const int GitTimeoutMilliseconds = 15_000;

    /// <summary>The author and committer name the repository is configured with.</summary>
    private const string IdentityName = "Test User";

    /// <summary>The author and committer email the repository is configured with.</summary>
    private const string IdentityEmail = "tester@example.com";

    /// <summary>Where <see cref="CommitWithForgedSignature"/> writes the commit text it hashes; inside <c>.git</c>, so the worktree stays clean.</summary>
    private const string ForgedCommitFile = ".git/forged-commit";

    /// <summary>An armor body that is not a real signature. git hands it to the verifier all the same.</summary>
    private const string ForgedSignatureBody = "Zm9yZ2VkIHNpZ25hdHVyZQ==";

    /// <summary>Where <see cref="ConfigureSignatureProgramTrap"/> writes the script <c>gpg.program</c> names.</summary>
    private const string TrapScriptFile = ".git/fake-gpg";

    /// <summary>Where <see cref="ConfigureLazyFetchTrap"/> writes the script <c>remote.origin.uploadpack</c> names.</summary>
    private const string UploadPackTrapFile = ".git/fake-upload-pack";

    /// <summary>Where <see cref="ArmFilterDriverTrap"/> writes the script a filter driver's <c>clean</c> names, under a repository's <c>.git</c>.</summary>
    private const string FilterTrapFile = ".git/fake-filter";

    /// <summary>The one file <see cref="AddSubmoduleWithFilterDriverTrap"/> commits in the submodule.</summary>
    private const string SubmoduleFile = "s.txt";

    /// <summary>The message of the commit that records a submodule's first gitlink.</summary>
    private const string SubmoduleAddMessage = "add submodule";

    /// <summary>The file <see cref="StageIgnoredSubmoduleMove"/> declares its submodule in.</summary>
    private const string GitmodulesFile = ".gitmodules";

    /// <summary>The index mode of a gitlink, the entry a submodule occupies in its superproject.</summary>
    private const string GitlinkMode = "160000";

    /// <summary>
    /// The commit <see cref="StageIgnoredSubmoduleMove"/> first records for its gitlink. A gitlink names a commit in
    /// another repository, so any 40-hex value works.
    /// </summary>
    private const string SubmoduleOldCommit = "1111111111111111111111111111111111111111";

    /// <summary>The commit <see cref="StageIgnoredSubmoduleMove"/> stages for its gitlink.</summary>
    private const string SubmoduleNewCommit = "2222222222222222222222222222222222222222";

    /// <summary>Appended to the script's path to name the marker file the script creates when it runs.</summary>
    private const string TrapMarkerSuffix = ".ran";

    /// <summary>
    /// The script a trap points git at: it creates its own path plus <see cref="TrapMarkerSuffix"/>, writes one line to
    /// stderr, and exits 1. LF endings, so the shebang line reads <c>/bin/sh</c>.
    /// </summary>
    private const string TrapScript = $"#!/bin/sh\n: > \"$0{TrapMarkerSuffix}\"\necho \"trap program ran\" >&2\nexit 1\n";

    /// <summary>
    /// How far <see cref="ArmFilterDriverTrap"/> and <see cref="MakeStatDirty"/> move a tracked file's modification
    /// time, so it no longer matches the time the index recorded and git hashes the file again.
    /// </summary>
    private static readonly TimeSpan StatDirtyShift = TimeSpan.FromMinutes(-10);

    public TempGitRepo()
    {
        var created = Path.Combine(Path.GetTempPath(), "mcp-gitops-temp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(created);
        RepoPath = RealPath(created);

        Run("init", "-b", "main");
        Run("config", "user.email", IdentityEmail);
        Run("config", "user.name", IdentityName);
        Run("config", "commit.gpgsign", "false");
    }

    /// <summary>The canonical absolute path of the repository root, as git reports it.</summary>
    public string RepoPath { get; }

    /// <summary>Run git with <paramref name="args"/> in the repository.</summary>
    /// <param name="args">The git arguments, one per element.</param>
    /// <returns>git's standard output without its trailing newline. It is read after git exits, so this suits commands with short output.</returns>
    /// <exception cref="InvalidOperationException">Thrown when git cannot start or exits non-zero; the message carries git's stderr and stdout.</exception>
    /// <exception cref="TimeoutException">Thrown when git does not exit within the timeout; the process is killed first.</exception>
    public string Run(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = RepoPath,
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("failed to start git");
        process.StandardInput.Close();
        if (!process.WaitForExit(GitTimeoutMilliseconds))
        {
            TryKill(process);
            throw new TimeoutException($"git {string.Join(' ', args)} did not exit within {GitTimeoutMilliseconds} ms");
        }

        if (process.ExitCode != 0)
        {
            var stderr = process.StandardError.ReadToEnd();
            var stdout = process.StandardOutput.ReadToEnd();
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed ({process.ExitCode}): {stderr} {stdout}");
        }

        return process.StandardOutput.ReadToEnd().TrimEnd('\n');
    }

    /// <summary>Write a text file at the <c>/</c>-separated <paramref name="relativePath"/>, creating parent directories.</summary>
    public void WriteFile(string relativePath, string content)
        => File.WriteAllText(PrepareTarget(relativePath), content);

    /// <summary>Write raw bytes at the <c>/</c>-separated <paramref name="relativePath"/>, creating parent directories.</summary>
    public void WriteBytes(string relativePath, byte[] content)
        => File.WriteAllBytes(PrepareTarget(relativePath), content);

    /// <summary>
    /// Merge <paramref name="branch"/> into the current branch where the merge is expected to stop on a conflict, leaving
    /// the conflicted paths unmerged in the index and marked up in the working tree.
    /// </summary>
    /// <param name="branch">The branch to merge.</param>
    /// <exception cref="InvalidOperationException">Thrown when the merge leaves no unmerged path.</exception>
    /// <exception cref="TimeoutException">Thrown when git does not exit within the timeout.</exception>
    public void MergeLeavingConflicts(string branch)
    {
        try
        {
            Run("merge", "--no-edit", branch);
        }
        catch (InvalidOperationException)
        {
            // git merge exits 1 when it stops on a conflict.
        }

        if (Run("ls-files", "--unmerged").Length == 0)
        {
            throw new InvalidOperationException($"merging {branch} left no unmerged path");
        }
    }

    /// <summary>
    /// Move <paramref name="trackedPath"/>'s modification time by <see cref="StatDirtyShift"/>, so the stat data the
    /// index recorded no longer matches the file while its content still does, then check that <c>git diff-files</c>
    /// lists the file. <c>git diff-files</c> reports such a file as changed and never writes the index, so the check
    /// leaves the file stat-dirty for the call under test.
    /// </summary>
    /// <param name="trackedPath">A <c>/</c>-separated path committed at <c>HEAD</c>, unchanged in the worktree.</param>
    /// <exception cref="InvalidOperationException">Thrown when git fails or does not list the file.</exception>
    /// <exception cref="TimeoutException">Thrown when git does not exit within the timeout.</exception>
    public void MakeStatDirty(string trackedPath)
    {
        var file = PrepareTarget(trackedPath);
        File.SetLastWriteTimeUtc(file, File.GetLastWriteTimeUtc(file) + StatDirtyShift);
        if (Run("diff-files", "--name-only", "--", trackedPath) != trackedPath)
        {
            throw new InvalidOperationException($"git diff-files does not list {trackedPath} after its modification time moved");
        }
    }

    /// <summary>
    /// Point the current branch at a new commit on top of <c>HEAD</c>, with the same tree, whose <c>gpgsig</c> header holds
    /// a PGP-armored block that is not a real signature. git treats any commit with that header as signed and hands the
    /// block to the verifier when it checks signatures, so no signing program is needed. SHA-1 repositories only; a
    /// SHA-256 one reads <c>gpgsig-sha256</c>.
    /// </summary>
    /// <param name="message">The commit message, also used as the reflog message for the branch update.</param>
    /// <returns>The new commit's id.</returns>
    public string CommitWithForgedSignature(string message)
    {
        var tree = Run("rev-parse", "HEAD^{tree}");
        var parent = Run("rev-parse", "HEAD");
        var identity = $"{IdentityName} <{IdentityEmail}> {DateTimeOffset.UtcNow.ToUnixTimeSeconds()} +0000";
        WriteFile(
            ForgedCommitFile,
            $"tree {tree}\nparent {parent}\nauthor {identity}\ncommitter {identity}\n"
            + $"gpgsig -----BEGIN PGP SIGNATURE-----\n \n {ForgedSignatureBody}\n -----END PGP SIGNATURE-----\n"
            + $"\n{message}\n");
        var commit = Run("hash-object", "-t", "commit", "-w", ForgedCommitFile);
        Run("update-ref", "-m", message, "HEAD", commit);
        return commit;
    }

    /// <summary>
    /// Configure the repository as a hostile one could: <c>log.showSignature=true</c>, and <c>gpg.program</c> naming a
    /// script that creates a marker file and writes one line to stderr whenever anything runs it. Then check that the trap
    /// fires by running <c>git log</c> once without the server's pin, so a trap test never passes on a machine where git
    /// cannot start the script: the calling test is skipped on Windows, where that depends on how Git for Windows was
    /// started, and fails anywhere else. Call it after <see cref="CommitWithForgedSignature"/>: the check logs
    /// <c>HEAD</c>.
    /// </summary>
    /// <returns>The marker file's path, with the file deleted again. It exists afterwards only if git ran the script.</returns>
    public string ConfigureSignatureProgramTrap()
    {
        var script = WriteTrapScript(TrapScriptFile);
        var marker = script + TrapMarkerSuffix;
        Run("config", "gpg.program", script);
        Run("config", "log.showSignature", "true");
        Run("log", "--max-count=1", "--format=%H");
        RequireTrapFired(marker, "git did not run the gpg.program script on this machine, so the signature trap cannot fire");
        File.Delete(marker);
        return marker;
    }

    /// <summary>
    /// Configure the repository as a hostile partial clone could: <c>origin</c> is a promisor remote at a local path whose
    /// <c>uploadpack</c> is a script that creates a marker file whenever anything runs it, and the blob
    /// <paramref name="trackedPath"/> has at <c>HEAD</c> is deleted from the object store, so the next read of that blob
    /// makes git start a lazy fetch from <c>origin</c>. Then check that the trap fires by listing <c>HEAD</c> with sizes
    /// once, without the server's environment, so a trap test never passes on a machine where git cannot start the script:
    /// the calling test is skipped on Windows, where git needs <c>sh</c> on its <c>PATH</c> to start it, and fails anywhere
    /// else.
    /// </summary>
    /// <param name="trackedPath">A <c>/</c>-separated path committed at <c>HEAD</c> whose blob is still a loose object.</param>
    /// <returns>The marker file's path, with the file deleted again. It exists afterwards only if git ran the script.</returns>
    public string ConfigureLazyFetchTrap(string trackedPath)
    {
        var blob = Run("rev-parse", $"HEAD:{trackedPath}");
        var script = WriteTrapScript(UploadPackTrapFile);
        var marker = script + TrapMarkerSuffix;
        Run("config", "remote.origin.url", RepoPath);
        Run("config", "remote.origin.promisor", "true");
        Run("config", "remote.origin.uploadpack", $"'{script.Replace('\\', '/')}'");

        var looseObject = Path.Combine(RepoPath, ".git", "objects", blob[..2], blob[2..]);
        File.SetAttributes(looseObject, FileAttributes.Normal);
        File.Delete(looseObject);

        Run("ls-tree", "-r", "-l", "HEAD");
        RequireTrapFired(marker, "git did not run the remote.origin.uploadpack script on this machine, so the lazy-fetch trap cannot fire");
        File.Delete(marker);
        return marker;
    }

    /// <summary>
    /// Configure the repository as a hostile one could: <c>filter.trap.clean</c> names a script that creates a marker file
    /// whenever anything runs it, <c>.git/info/attributes</c> selects the driver for every path, and
    /// <paramref name="trackedPath"/> gets a modification time the index did not record, so the next status hashes it
    /// through the filter. Then check that the trap fires by running <c>git --no-optional-locks status</c> once, without
    /// the server's pins (with optional locks, that status would write the refreshed index and the file would stop being
    /// stat-dirty), so a trap test never passes on a machine where git cannot start the script: the calling test is
    /// skipped on Windows, where git needs <c>sh</c> on its <c>PATH</c> to start it, and fails anywhere else. The check
    /// fires through <c>git status</c>, and a worktree <c>git diff</c> or a <c>git blame</c> without a revision reach the
    /// same clean conversion, so it gates those callers as well.
    /// </summary>
    /// <param name="trackedPath">A <c>/</c>-separated path committed at <c>HEAD</c>, unchanged in the worktree.</param>
    /// <returns>The marker file's path, with the file deleted again. It exists afterwards only if git ran the script.</returns>
    public string ConfigureFilterDriverTrap(string trackedPath)
    {
        var marker = ArmFilterDriverTrap(string.Empty, "trap", trackedPath);
        Run("--no-optional-locks", "status", "--porcelain");
        RequireTrapFired(marker, "git did not run the filter.trap.clean script on this machine, so the filter-driver trap cannot fire");
        File.Delete(marker);
        return marker;
    }

    /// <summary>
    /// Add a checked-out submodule at <paramref name="submodulePath"/> as a hostile repository could: a repository of its
    /// own with one committed file, recorded in this repository as a committed gitlink, whose own config and
    /// <c>.git/info/attributes</c> select a <c>filter.subtrap.clean</c> script that creates a marker file, and whose file
    /// gets a modification time the submodule's index did not record. A status here then runs <c>git status</c> inside the
    /// submodule, which hashes that file through the submodule's filter. Check that the trap fires the same way
    /// <see cref="ConfigureFilterDriverTrap"/> does; a <c>git diff</c> against the working tree starts the submodule's
    /// <c>git status</c> from the same check, so the status-based self-check gates diff callers as well.
    /// </summary>
    /// <param name="submodulePath">A <c>/</c>-separated path that does not exist yet.</param>
    /// <returns>The marker file's path, with the file deleted again. It exists afterwards only if git ran the script.</returns>
    public string AddSubmoduleWithFilterDriverTrap(string submodulePath)
    {
        Run("init", "-b", "main", submodulePath);
        Run("-C", submodulePath, "config", "user.email", IdentityEmail);
        Run("-C", submodulePath, "config", "user.name", IdentityName);
        Run("-C", submodulePath, "config", "commit.gpgsign", "false");
        WriteFile($"{submodulePath}/{SubmoduleFile}", "sub\n");
        Run("-C", submodulePath, "add", SubmoduleFile);
        Run("-C", submodulePath, "commit", "-m", "submodule");
        Run("add", submodulePath);
        Run("commit", "-m", SubmoduleAddMessage);

        var marker = ArmFilterDriverTrap($"{submodulePath}/", "subtrap", SubmoduleFile);
        Run("--no-optional-locks", "status", "--porcelain");
        RequireTrapFired(marker, "git did not run the submodule's filter.subtrap.clean script on this machine, so the submodule filter-driver trap cannot fire");
        File.Delete(marker);
        return marker;
    }

    /// <summary>
    /// Set up a submodule the way a hostile repository can hide one: an empty directory at <paramref name="path"/>, as a
    /// clone whose submodules were never initialized has; a committed <c>.gitmodules</c> that declares the submodule there
    /// with <c>ignore = all</c>; and a committed gitlink there at <see cref="SubmoduleOldCommit"/>. Then stage the gitlink
    /// at <see cref="SubmoduleNewCommit"/>, so the next commit moves the submodule. No submodule repository exists:
    /// <c>git update-index --cacheinfo</c> records a gitlink without checking the commit it names.
    /// </summary>
    /// <param name="path">A <c>/</c>-separated path that does not exist yet; also the submodule's name.</param>
    /// <returns>The commit id the staged gitlink names.</returns>
    public string StageIgnoredSubmoduleMove(string path)
    {
        Directory.CreateDirectory(PrepareTarget(path));
        WriteFile(GitmodulesFile, $"[submodule \"{path}\"]\n\tpath = {path}\n\tignore = all\n");
        Run("add", GitmodulesFile);
        Run("update-index", "--add", "--cacheinfo", $"{GitlinkMode},{SubmoduleOldCommit},{path}");
        Run("commit", "-m", SubmoduleAddMessage);
        Run("update-index", "--cacheinfo", $"{GitlinkMode},{SubmoduleNewCommit},{path}");
        return SubmoduleNewCommit;
    }

    public void Dispose()
    {
        if (!Directory.Exists(RepoPath))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(RepoPath, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(RepoPath, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup; anything left over lives under the OS temp directory.
        }
        catch (UnauthorizedAccessException)
        {
            // Same best-effort contract.
        }
    }

    /// <summary>Kill a git process that outlived the timeout, so the timeout stays the reported failure even if the kill itself fails.</summary>
    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Win32Exception)
        {
            // The process was already terminating.
        }
        catch (AggregateException)
        {
            // A descendant could not be terminated; the caller still reports the timeout.
        }
    }

    /// <summary>Write <see cref="TrapScript"/> at the <c>/</c>-separated <paramref name="relativePath"/>, executable by its owner off Windows.</summary>
    /// <param name="relativePath">The script's <c>/</c>-separated path under the repository root.</param>
    /// <returns>The script's absolute path.</returns>
    private string WriteTrapScript(string relativePath)
    {
        var script = PrepareTarget(relativePath);
        File.WriteAllText(script, TrapScript);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return script;
    }

    /// <summary>Return when <paramref name="marker"/> exists; otherwise skip the calling test on Windows and fail it anywhere else.</summary>
    /// <param name="marker">The marker file the trap script creates when git runs it.</param>
    /// <param name="reason">The skip or failure message.</param>
    private static void RequireTrapFired(string marker, string reason)
    {
        if (File.Exists(marker))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Assert.Skip(reason);
        }

        Assert.Fail(reason);
    }

    /// <summary>
    /// In the repository at <paramref name="repoPrefix"/>, point <c>filter.&lt;driver&gt;.clean</c> at a trap script, select
    /// the driver for every path through that repository's <c>.git/info/attributes</c>, and move
    /// <paramref name="trackedPath"/>'s modification time by <see cref="StatDirtyShift"/>.
    /// </summary>
    /// <param name="repoPrefix">Empty for this repository, or a submodule's <c>/</c>-separated path with a trailing <c>/</c>.</param>
    /// <param name="driver">The driver name to configure and select.</param>
    /// <param name="trackedPath">A file committed in that repository, relative to its root.</param>
    /// <returns>The path of the marker file the script creates when git runs it.</returns>
    private string ArmFilterDriverTrap(string repoPrefix, string driver, string trackedPath)
    {
        var script = WriteTrapScript(repoPrefix + FilterTrapFile);
        var repoDirectory = Path.Combine(RepoPath, repoPrefix.Replace('/', Path.DirectorySeparatorChar));
        Run("-C", repoDirectory, "config", $"filter.{driver}.clean", $"'{script.Replace('\\', '/')}'");
        WriteFile($"{repoPrefix}.git/info/attributes", $"* filter={driver}\n");
        var file = PrepareTarget(repoPrefix + trackedPath);
        File.SetLastWriteTimeUtc(file, File.GetLastWriteTimeUtc(file) + StatDirtyShift);
        return script + TrapMarkerSuffix;
    }

    private string PrepareTarget(string relativePath)
    {
        var full = Path.Combine(RepoPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        return full;
    }

    /// <summary>
    /// Resolve every symlinked component of an existing path to its real target, root-downward, so the
    /// result matches the path git reports for the directory (<c>git rev-parse --show-toplevel</c>). On
    /// macOS the temp directory sits under <c>/var</c>, a symlink to <c>/private/var</c>; on Windows and
    /// Linux temp directories this is usually a no-op.
    /// </summary>
    private static string RealPath(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? string.Empty;
        var current = root;
        foreach (var segment in full[root.Length..]
                     .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current))
            {
                continue;
            }

            var resolved = new DirectoryInfo(current).ResolveLinkTarget(returnFinalTarget: true);
            if (resolved is not null)
            {
                current = resolved.FullName;
            }
        }

        return current;
    }
}