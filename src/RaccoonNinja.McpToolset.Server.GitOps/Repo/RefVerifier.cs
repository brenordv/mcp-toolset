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
        if (string.IsNullOrWhiteSpace(sha))
        {
            throw new RefNotFoundException("ref resolved to empty output",
                new Dictionary<string, object> { ["param"] = "ref" });
        }

        // Postcondition: `rev-parse --verify` yields exactly one full object name. Assert that here so
        // a resolved ref is always a clean SHA before it is composed into a positional argument (range
        // reconstruction joins two of these with a literal `..`/`...`), rather than trusting the shape.
        if (!IsResolvedObjectName(sha))
        {
            throw new RefNotFoundException("ref resolved to a non-object-name",
                new Dictionary<string, object> { ["param"] = "ref" });
        }

        return sha;
    }

    /// <summary>
    /// True when <paramref name="value"/> is a full git object name: lowercase hex of length 40
    /// (SHA-1) or 64 (SHA-256). Length-agnostic between the two hash algorithms so SHA-256 repos are
    /// not rejected.
    /// </summary>
    /// <param name="value">The trimmed <c>rev-parse --verify</c> output.</param>
    /// <returns><c>true</c> when the value is a full lowercase-hex object name.</returns>
    internal static bool IsResolvedObjectName(string value)
    {
        if (value is not { Length: 40 or 64 })
        {
            return false;
        }

        foreach (var c in value)
        {
            if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateRefShape(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
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