using System.Diagnostics;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;

/// <summary>
/// Creates a disposable temp git repository with a known commit history.
/// Tests that exercise the runner, repo resolver, or tool methods share it
/// via a collection fixture to amortize setup cost.
/// </summary>
public sealed class GitRepoFixture : IAsyncLifetime
{
    public string RepoPath { get; private set; }

    public ValueTask InitializeAsync()
    {
        var created = Path.Combine(Path.GetTempPath(), "mcp-gitops-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(created);
        // Canonicalize through symlinks so the path matches what git reports for
        // this directory (rev-parse --show-toplevel). On macOS Path.GetTempPath()
        // lives under /var, a symlink to /private/var, so the raw temp path and
        // git's resolved path would otherwise differ.
        RepoPath = RealPath(created);

        Run("init", "-b", "main");
        Run("config", "user.email", "tester@example.com");
        Run("config", "user.name", "Test User");
        Run("config", "commit.gpgsign", "false");

        File.WriteAllText(Path.Combine(RepoPath, "README.md"), "# Test repo\n");
        Run("add", "README.md");
        Run("commit", "-m", "initial commit");

        File.WriteAllText(Path.Combine(RepoPath, "alpha.txt"), "alpha 1\nalpha 2\nalpha 3\n");
        File.WriteAllText(Path.Combine(RepoPath, "beta.txt"), "beta\n");
        Run("add", "alpha.txt", "beta.txt");
        Run("commit", "-m", "add alpha and beta");

        File.WriteAllText(Path.Combine(RepoPath, "alpha.txt"), "alpha 1\nalpha changed\nalpha 3\n");
        Run("add", "alpha.txt");
        Run("commit", "-m", "tweak alpha");

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        TryDelete(RepoPath);
        return ValueTask.CompletedTask;
    }

    private void Run(params string[] args)
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
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi);
        if (process == null) throw new InvalidOperationException("failed to start git");
        process.StandardInput.Close();
        process.WaitForExit(15_000);
        if (process.ExitCode != 0)
        {
            var err = process.StandardError.ReadToEnd();
            var outs = process.StandardOutput.ReadToEnd();
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed ({process.ExitCode}): {err} {outs}");
        }
    }

    /// <summary>
    /// Resolve every symlinked component of an existing path to its real target, root-downward, so the
    /// result matches git's canonicalized output. A no-op where no ancestor is a symlink, which is the
    /// usual case on Windows and Linux temp directories.
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

    private static void TryDelete(string path)
    {
        if (!Directory.Exists(path)) return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); }
                catch { /* ignore */ }
            }
            Directory.Delete(path, recursive: true);
        }
        catch { /* best effort */ }
    }
}

[CollectionDefinition(nameof(GitRepoCollection))]
public sealed class GitRepoCollection : ICollectionFixture<GitRepoFixture> { }