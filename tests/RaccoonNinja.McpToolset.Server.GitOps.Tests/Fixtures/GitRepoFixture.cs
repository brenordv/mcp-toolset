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
        RepoPath = Path.Combine(Path.GetTempPath(), "mcp-gitops-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RepoPath);

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