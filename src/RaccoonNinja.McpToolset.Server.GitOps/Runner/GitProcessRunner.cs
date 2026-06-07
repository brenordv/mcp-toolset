using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;

namespace RaccoonNinja.McpToolset.Server.GitOps.Runner;

/// <summary>
/// Hardened subprocess executor. Uses <see cref="ProcessStartInfo.ArgumentList"/>
/// (never the joined string) so per-token quoting cannot leak meta-characters
/// to the shell. Stdout/stderr are drained concurrently with a per-stream byte cap
/// (truncate-then-flag), and a hard wall-clock timeout kills the process.
/// </summary>
public sealed class GitProcessRunner(ILogger<GitProcessRunner> logger) : IGitProcessRunner
{
    private const int DefaultTimeoutMs = 30_000;
    private const int DefaultOutputCapBytes = 8 * 1024 * 1024;

    private const int ReadChunkSize = 64 * 1024;

    public async Task<RunResult> RunAsync(
        IList<string> argv,
        IDictionary<string, string> env,
        string workingDirectory,
        int? timeoutMs = null,
        int? outputCapBytes = null,
        int? callId = null,
        CancellationToken cancellationToken = default)
    {
        if (argv == null || argv.Count == 0)
            throw new ArgumentException("argv must contain at least the executable", nameof(argv));

        var effectiveTimeout = timeoutMs ?? DefaultTimeoutMs;
        var capBytes = outputCapBytes ?? DefaultOutputCapBytes;

        var psi = new ProcessStartInfo
        {
            FileName = argv[0],
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        };

        for (var i = 1; i < argv.Count; i++)
        {
            psi.ArgumentList.Add(argv[i]);
        }

        psi.Environment.Clear();
        foreach (var kvp in env)
        {
            psi.Environment[kvp.Key] = kvp.Value;
        }

        var sw = Stopwatch.StartNew();
        Process process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
        {
            throw new GitNotInstalledException(
                $"git executable not found on PATH ({argv[0]})",
                new Dictionary<string, object> { ["executable"] = argv[0] });
        }

        if (process == null)
        {
            throw new GitNotInstalledException(
                $"failed to start git process ({argv[0]})",
                new Dictionary<string, object> { ["executable"] = argv[0] });
        }

        // Close stdin immediately so git never blocks on it.
        try { process.StandardInput.Close(); } catch { /* ignore */ }

        var stdoutBuf = new CappedBuffer(capBytes);
        var stderrBuf = new CappedBuffer(capBytes);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(effectiveTimeout);

        var stdoutTask = PumpAsync(process.StandardOutput.BaseStream, stdoutBuf, timeoutCts.Token);
        var stderrTask = PumpAsync(process.StandardError.BaseStream, stderrBuf, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new GitTimeoutException(
                $"git subprocess exceeded {effectiveTimeout / 1000.0:F1}s timeout; try narrower filters",
                new Dictionary<string, object> { ["timeout_ms"] = effectiveTimeout });
        }
        finally
        {
            sw.Stop();
        }

        var truncated = stdoutBuf.Truncated || stderrBuf.Truncated;
        var stderrBytes = stderrBuf.ToArray();

        FilterTripwire.Inspect(stderrBytes, argv, callId, logger);

        return new RunResult
        {
            Argv = [.. argv],
            ExitCode = process.ExitCode,
            Stdout = stdoutBuf.ToArray(),
            Stderr = stderrBytes,
            DurationMs = (int)sw.ElapsedMilliseconds,
            Truncated = truncated,
        };
    }

    private static void TryKill(Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch { /* swallow. process may already be gone */ }
        try { process.WaitForExit(1000); }
        catch { /* swallow */ }
    }

    private static async Task PumpAsync(Stream stream, CappedBuffer sink, CancellationToken cancellationToken)
    {
        var buffer = new byte[ReadChunkSize];
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                if (!sink.Append(buffer, read))
                {
                    continue;
                }

                // Drain remaining without storing it so the child doesn't block.
                while (true)
                {
                    var drained = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                    if (drained == 0) break;
                }
                break;
            }
        }
        catch (IOException) { /* pipe closed mid-read; exit code reports the rest */ }
    }

    private sealed class CappedBuffer(int cap)
    {
        private readonly byte[] _buf = new byte[cap];
        private int _size;
        public bool Truncated { get; private set; }

        public bool Append(byte[] chunk, int count)
        {
            if (_size >= _buf.Length)
            {
                Truncated = true;
                return true;
            }
            var remaining = _buf.Length - _size;
            if (count <= remaining)
            {
                Buffer.BlockCopy(chunk, 0, _buf, _size, count);
                _size += count;
                return false;
            }
            Buffer.BlockCopy(chunk, 0, _buf, _size, remaining);
            _size = _buf.Length;
            Truncated = true;
            return true;
        }

        public byte[] ToArray()
        {
            var slice = new byte[_size];
            Buffer.BlockCopy(_buf, 0, slice, 0, _size);
            return slice;
        }
    }
}