using System.Diagnostics;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;

/// <summary>
/// A fresh, empty temp git repository (branch <c>main</c>, local identity, no signing) for tests that
/// need a history of their own rather than the shared linear <see cref="GitRepoFixture"/>. The test
/// builds its history with <see cref="WriteFile"/>, <see cref="WriteBytes"/>, and <see cref="Run"/>.
/// </summary>
internal sealed class TempGitRepo : IDisposable
{
    public TempGitRepo()
    {
        var created = Path.Combine(Path.GetTempPath(), "mcp-gitops-temp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(created);
        RepoPath = GitRepoFixture.RealPath(created);

        Run("init", "-b", "main");
        Run("config", "user.email", "tester@example.com");
        Run("config", "user.name", "Test User");
        Run("config", "commit.gpgsign", "false");
    }

    /// <summary>The canonical absolute path of the repository root, as git reports it.</summary>
    public string RepoPath { get; }

    /// <summary>Run git with <paramref name="args"/> in the repository; throws carrying stderr on a non-zero exit.</summary>
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
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi);
        if (process == null) throw new InvalidOperationException("failed to start git");
        process.StandardInput.Close();
        process.WaitForExit(15_000);
        if (process.ExitCode != 0)
        {
            var err = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed ({process.ExitCode}): {err}");
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
        if (!Directory.Exists(RepoPath)) return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(RepoPath, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); }
                catch { /* ignore */ }
            }
            Directory.Delete(RepoPath, recursive: true);
        }
        catch { /* best effort */ }
    }

    private string PrepareTarget(string relativePath)
    {
        var full = Path.Combine(RepoPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        return full;
    }
}