namespace RaccoonNinja.McpToolset.Server.GitOps.Runner;

/// <summary>Outcome of a single git subprocess invocation.</summary>
public sealed record RunResult
{
    public IReadOnlyList<string> Argv { get; init; }
    public int ExitCode { get; init; }
    public byte[] Stdout { get; init; }
    public byte[] Stderr { get; init; }
    public int DurationMs { get; init; }
    public bool Truncated { get; init; }
}