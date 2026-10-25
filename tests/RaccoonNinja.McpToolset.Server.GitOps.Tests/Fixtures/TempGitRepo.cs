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

    /// <summary>Appended to the script's path to name the marker file the script creates when it runs.</summary>
    private const string TrapMarkerSuffix = ".ran";

    /// <summary>
    /// The script <c>gpg.program</c> names: it creates its own path plus <see cref="TrapMarkerSuffix"/> and writes one line
    /// to stderr, which git prints into its output in front of the signed commit. LF endings, so the shebang line reads
    /// <c>/bin/sh</c>.
    /// </summary>
    private const string TrapScript = $"#!/bin/sh\n: > \"$0{TrapMarkerSuffix}\"\necho \"signature program ran\" >&2\nexit 1\n";

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
        var script = PrepareTarget(TrapScriptFile);
        var marker = script + TrapMarkerSuffix;
        File.WriteAllText(script, TrapScript);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        Run("config", "gpg.program", script);
        Run("config", "log.showSignature", "true");
        Run("log", "--max-count=1", "--format=%H");
        if (!File.Exists(marker))
        {
            const string reason = "git did not run the gpg.program script on this machine, so the signature trap cannot fire";
            if (OperatingSystem.IsWindows())
            {
                Assert.Skip(reason);
            }

            Assert.Fail(reason);
        }

        File.Delete(marker);
        return marker;
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