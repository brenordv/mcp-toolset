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
            if (string.IsNullOrWhiteSpace(reference))
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

    /// <summary>
    /// Verify a value that may be a git range expression (<c>A..B</c> / <c>A...B</c>). A plain ref is
    /// resolved via <see cref="IRefVerifier.VerifyAsync"/> exactly as before; a range has each side
    /// resolved independently (an omitted side defaults to <c>HEAD</c>) and is reassembled as
    /// <c>&lt;leftSha&gt;&lt;operator&gt;&lt;rightSha&gt;</c>, so the token handed to git is built only
    /// from verified object names joined by a literal operator.
    /// </summary>
    /// <param name="verifier">The verifier to resolve refs with.</param>
    /// <param name="repoRoot">The resolved repository root.</param>
    /// <param name="reference">A single ref or a two-/three-dot range expression.</param>
    /// <param name="cancellationToken">Token to cancel the verification calls.</param>
    /// <returns>The verified SHA, or the reconstructed <c>sha operator sha</c> range token.</returns>
    /// <exception cref="RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions.RejectedArgumentException">
    /// <paramref name="reference"/> is a malformed range expression (see <see cref="RefRange.Parse"/>).
    /// </exception>
    public static async Task<string> VerifyRefOrRangeAsync(
        this IRefVerifier verifier,
        string repoRoot,
        string reference,
        CancellationToken cancellationToken = default)
    {
        if (RefRange.Parse(reference) is not { } range)
        {
            return await verifier.VerifyAsync(reference, repoRoot, cancellationToken).ConfigureAwait(false);
        }

        var leftRef = range.Left.Length > 0 ? range.Left : "HEAD";
        var rightRef = range.Right.Length > 0 ? range.Right : "HEAD";
        var leftSha = await verifier.VerifyAsync(leftRef, repoRoot, cancellationToken).ConfigureAwait(false);
        var rightSha = await verifier.VerifyAsync(rightRef, repoRoot, cancellationToken).ConfigureAwait(false);
        return $"{leftSha}{range.Operator}{rightSha}";
    }

    /// <summary>
    /// Verify each non-empty entry in <paramref name="references"/> as a ref or range expression via
    /// <see cref="VerifyRefOrRangeAsync"/>, preserving order and skipping blanks. Blank or null inputs
    /// yield an empty list, never null.
    /// </summary>
    /// <param name="verifier">The verifier to resolve refs with.</param>
    /// <param name="repoRoot">The resolved repository root.</param>
    /// <param name="references">The candidate refs or ranges; null/empty entries are skipped.</param>
    /// <param name="cancellationToken">Token to cancel the verification calls.</param>
    /// <returns>The verified SHAs / range tokens, in input order.</returns>
    /// <exception cref="RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions.RejectedArgumentException">
    /// An entry is a malformed range expression.
    /// </exception>
    public static async Task<List<string>> VerifyOptionalRefsOrRangesAsync(
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
            if (string.IsNullOrWhiteSpace(reference))
            {
                continue;
            }

            verified.Add(await verifier.VerifyRefOrRangeAsync(repoRoot, reference, cancellationToken).ConfigureAwait(false));
        }

        return verified;
    }
}