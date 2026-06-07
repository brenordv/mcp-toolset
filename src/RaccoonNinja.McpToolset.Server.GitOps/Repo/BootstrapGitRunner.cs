using System.ComponentModel;
using System.Diagnostics;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Security;

namespace RaccoonNinja.McpToolset.Server.GitOps.Repo;

/// <summary>
/// Spawns the bootstrap <c>git rev-parse</c> calls used by <see cref="RepoRootResolver"/>
/// and <see cref="RefVerifier"/>. Both call sites need the Layer 2 hardening but
/// cannot route through <see cref="GitCommandBuilder"/> (chicken-and-egg: the
/// builder requires the resolved repo root). This helper centralizes the
/// duplicated Process setup so the bootstrap exception lives in exactly one place.
/// </summary>
internal static class BootstrapGitRunner
{
    /// <summary>Run <c>git [hardening...] -C cwd tail...</c>; return (stdout, exitCode).</summary>
    public static async Task<(string Stdout, int ExitCode)> RunAsync(
        string gitExecutable,
        string cwd,
        IReadOnlyList<string> tail,
        TimeSpan timeout,
        Action onTimeout,
        CancellationToken cancellationToken)
    {
        var argv = BuildArgv(gitExecutable, cwd, tail);
        var process = StartProcess(argv);

        // Close stdin immediately so git never blocks on it.
        try { process.StandardInput.Close(); } catch { /* ignore */ }

        return await ReadToExitAsync(process, timeout, onTimeout, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Compose <c>git [hardening...] -C cwd tail...</c> into a full argv list.</summary>
    private static List<string> BuildArgv(string gitExecutable, string cwd, IReadOnlyList<string> tail)
    {
        var argv = new List<string> { gitExecutable };
        argv.AddRange(GitCommandBuilder.HardeningArgvPrefix());
        argv.Add("-C");
        argv.Add(cwd);
        argv.AddRange(tail);
        return argv;
    }

    /// <summary>
    /// Spawn the hardened git subprocess with stdin/stdout/stderr redirected and a
    /// scrubbed environment. Translates spawn failures into <see cref="GitNotInstalledException"/>.
    /// </summary>
    private static Process StartProcess(List<string> argv)
    {
        var psi = new ProcessStartInfo
        {
            FileName = argv[0],
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        for (var i = 1; i < argv.Count; i++) psi.ArgumentList.Add(argv[i]);

        psi.Environment.Clear();
        foreach (var kvp in EnvironmentBuilder.Build()) psi.Environment[kvp.Key] = kvp.Value;

        Process process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
        {
            throw new GitNotInstalledException(
                "git executable not found on PATH",
                new Dictionary<string, object> { ["executable"] = argv[0] });
        }

        return process ?? throw new GitNotInstalledException(
            "failed to start git bootstrap process",
            new Dictionary<string, object> { ["executable"] = argv[0] });
    }

    /// <summary>
    /// Read stdout to completion while waiting for exit under a wall-clock timeout. On timeout,
    /// kill the process tree, invoke <paramref name="onTimeout"/>, and rethrow the cancellation.
    /// </summary>
    private static async Task<(string Stdout, int ExitCode)> ReadToExitAsync(
        Process process,
        TimeSpan timeout,
        Action onTimeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            return (stdout, process.ExitCode);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            onTimeout?.Invoke();
            throw;
        }
    }
}