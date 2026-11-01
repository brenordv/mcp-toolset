using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;

/// <summary>
/// Runs git for real and hands back an empty standard output for each process <paramref name="drops"/> picks by its argv,
/// so the tool sees output it cannot read while the exit code stays git's.
/// </summary>
/// <param name="drops">Picks, by argv, the processes whose standard output is dropped.</param>
internal sealed class StdoutDroppingRunner(Func<IList<string>, bool> drops) : IGitProcessRunner
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
        return drops(argv) ? result with { Stdout = [] } : result;
    }
}