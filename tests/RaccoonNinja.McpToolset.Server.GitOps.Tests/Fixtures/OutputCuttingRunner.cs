using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;

/// <summary>
/// Runs git for real and, for each process <paramref name="cuts"/> picks by its argv, keeps only the first
/// <paramref name="keptBytes"/> bytes of standard output and marks the result truncated, as the output cap does.
/// </summary>
/// <param name="cuts">Picks, by argv, the processes whose standard output is cut.</param>
/// <param name="keptBytes">How many leading bytes of a picked process's standard output are kept.</param>
internal sealed class OutputCuttingRunner(Func<IList<string>, bool> cuts, int keptBytes) : IGitProcessRunner
{
    private readonly GitProcessRunner _inner = new(NullLogger<GitProcessRunner>.Instance);

    public async Task<RunResult> RunAsync(
        IList<string> argv,
        IDictionary<string, string> env,
        string workingDirectory,
        int? timeoutMs = null,
        int? outputCapBytes = null,
        int? callId = null,
        byte[] standardInput = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _inner.RunAsync(argv, env, workingDirectory, timeoutMs, outputCapBytes, callId, standardInput, cancellationToken);
        return cuts(argv) ? result with { Stdout = result.Stdout[..Math.Min(keptBytes, result.Stdout.Length)], Truncated = true } : result;
    }
}