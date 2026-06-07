namespace RaccoonNinja.McpToolset.Server.GitOps.Runner;

/// <summary>Seam for tests; only one production implementation exists.</summary>
public interface IGitProcessRunner
{
    Task<RunResult> RunAsync(
        IList<string> argv,
        IDictionary<string, string> env,
        string workingDirectory,
        int? timeoutMs = null,
        int? outputCapBytes = null,
        int? callId = null,
        CancellationToken cancellationToken = default);
}