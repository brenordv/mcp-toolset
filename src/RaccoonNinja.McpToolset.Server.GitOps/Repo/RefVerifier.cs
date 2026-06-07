using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;

namespace RaccoonNinja.McpToolset.Server.GitOps.Repo;

/// <summary>
/// Resolves a ref via <c>git rev-parse --verify --end-of-options &lt;ref&gt;</c>
/// under the Layer 2 hardened env. Bootstrap exception: cannot use
/// <see cref="Security.GitCommandBuilder"/> because the builder would re-require the repo root.
/// </summary>
public sealed class RefVerifier(string gitExecutable = "git") : IRefVerifier
{
    private static readonly TimeSpan BootstrapTimeout = TimeSpan.FromSeconds(10);

    public async Task<string> VerifyAsync(string reference, string repoRoot, CancellationToken cancellationToken = default)
    {
        ValidateRefShape(reference);

        var (stdout, exitCode) = await BootstrapGitRunner.RunAsync(
            gitExecutable,
            repoRoot,
            new[] { "rev-parse", "--verify", "--end-of-options", reference },
            BootstrapTimeout,
            onTimeout: null,
            cancellationToken).ConfigureAwait(false);

        if (exitCode != 0)
            throw new RefNotFoundException("ref not found; try git_branch_list or git_log",
                new Dictionary<string, object> { ["param"] = "ref" });

        var sha = stdout.Trim();
        return string.IsNullOrEmpty(sha)
            ? throw new RefNotFoundException("ref resolved to empty output",
                new Dictionary<string, object> { ["param"] = "ref" })
            : sha;
    }

    private static void ValidateRefShape(string reference)
    {
        if (string.IsNullOrEmpty(reference))
            throw new RejectedArgumentException("ref must be a non-empty string",
                new Dictionary<string, object> { ["param"] = "ref" });
        if (reference.Contains('\0'))
            throw new RejectedArgumentException("ref contains a control character",
                new Dictionary<string, object> { ["param"] = "ref" });
        foreach (var c in reference)
        {
            if (c < 0x20 && c != '\t')
                throw new RejectedArgumentException("ref contains a control character",
                    new Dictionary<string, object> { ["param"] = "ref" });
        }
        if (reference[0] == '-')
            throw new RejectedArgumentException("ref must not begin with '-'",
                new Dictionary<string, object> { ["param"] = "ref" });
    }
}