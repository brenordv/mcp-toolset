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

    public TempGitRepo()
    {
        var created = Path.Combine(Path.GetTempPath(), "mcp-gitops-temp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(created);
        RepoPath = RealPath(created);

        Run("init", "-b", "main");
        Run("config", "user.email", "tester@example.com");
        Run("config", "user.name", "Test User");
        Run("config", "commit.gpgsign", "false");
    }

    /// <summary>The canonical absolute path of the repository root, as git reports it.</summary>
    public string RepoPath { get; }

    /// <summary>Run git with <paramref name="args"/> in the repository.</summary>
    /// <param name="args">The git arguments, one per element.</param>
    /// <exception cref="InvalidOperationException">Thrown when git cannot start or exits non-zero; the message carries git's stderr and stdout.</exception>
    /// <exception cref="TimeoutException">Thrown when git does not exit within the timeout; the process is killed first.</exception>
    public void Run(params string[] args)
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
    }

    /// <summary>Write a text file at the <c>/</c>-separated <paramref name="relativePath"/>, creating parent directories.</summary>
    public void WriteFile(string relativePath, string content)
        => File.WriteAllText(PrepareTarget(relativePath), content);

    /// <summary>Write raw bytes at the <c>/</c>-separated <paramref name="relativePath"/>, creating parent directories.</summary>
    public void WriteBytes(string relativePath, byte[] content)
        => File.WriteAllBytes(PrepareTarget(relativePath), content);

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