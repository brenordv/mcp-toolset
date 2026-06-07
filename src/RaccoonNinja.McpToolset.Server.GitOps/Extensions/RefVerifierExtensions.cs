using RaccoonNinja.McpToolset.Server.GitOps.Repo;

namespace RaccoonNinja.McpToolset.Server.GitOps.Extensions;

/// <summary>
/// Shared ref-verification idioms layered over <see cref="IRefVerifier"/>, so every tool resolves
/// optional and required refs the same way instead of re-implementing the skip-blank loop inline.
/// </summary>
public static class RefVerifierExtensions
{
    /// <summary>
    /// Verify each non-empty reference in <paramref name="references"/> via <see cref="IRefVerifier.VerifyAsync"/>,
    /// preserving order and skipping blanks. Blank or null inputs yield an empty list, never null.
    /// </summary>
    /// <param name="verifier">The verifier to resolve refs with.</param>
    /// <param name="repoRoot">The resolved repository root.</param>
    /// <param name="references">The candidate refs; null/empty entries are skipped.</param>
    /// <param name="cancellationToken">Token to cancel the verification calls.</param>
    /// <returns>The verified SHAs, in input order.</returns>
    public static async Task<List<string>> VerifyOptionalRefsAsync(
        this IRefVerifier verifier,
        string repoRoot,
        IReadOnlyList<string> references,
        CancellationToken cancellationToken = default)
    {
        var verified = new List<string>();
        if (references is null)
        {
            return verified;
        }

        foreach (var reference in references)
        {
            if (string.IsNullOrEmpty(reference))
            {
                continue;
            }

            verified.Add(await verifier.VerifyAsync(reference, repoRoot, cancellationToken).ConfigureAwait(false));
        }

        return verified;
    }

    /// <summary>
    /// Verify a mandatory <paramref name="reference"/>, letting the verifier reject a blank value
    /// with its standard ref-not-found error rather than silently skipping it.
    /// </summary>
    /// <param name="verifier">The verifier to resolve the ref with.</param>
    /// <param name="repoRoot">The resolved repository root.</param>
    /// <param name="reference">The required ref; a null is normalized to empty so the verifier rejects it.</param>
    /// <param name="cancellationToken">Token to cancel the verification call.</param>
    /// <returns>The verified SHA.</returns>
    public static Task<string> VerifyRequiredRefAsync(
        this IRefVerifier verifier,
        string repoRoot,
        string reference,
        CancellationToken cancellationToken = default)
        => verifier.VerifyAsync(reference ?? string.Empty, repoRoot, cancellationToken);
}