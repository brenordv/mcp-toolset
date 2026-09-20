using RaccoonNinja.McpToolset.Server.FileVault.Configuration;
using RaccoonNinja.McpToolset.Server.FileVault.Storage;

namespace RaccoonNinja.McpToolset.Server.FileVault.Services;

/// <summary>
/// The pure body-search logic for <c>vault_search</c>, free of I/O so it is unit-testable without a
/// store. Terms are case-insensitive ordinal substrings against the body only. Matching mirrors
/// <c>vault_list</c>: every term must match (all-terms) first, and only when nothing matches every
/// term and there are at least two terms does a ranked any-term pass run. Callers that stream one
/// body at a time use <see cref="MatchDocument"/> plus <see cref="SelectAndRank"/> to keep peak
/// memory at a single body; <see cref="Search"/> is the all-in-memory convenience over the same
/// primitives.
/// </summary>
public static class ContentSearcher
{
    private static readonly IComparer<SearchCandidateRow> CandidateComparer = Comparer<SearchCandidateRow>.Create(
        static (a, b) => ListOrdering.CompareKeys(a.UpdatedAt, a.Name, a.Project, b.UpdatedAt, b.Name, b.Project));

    /// <summary>
    /// Split a query into at most <see cref="VaultConfig.MaxQueryTerms"/> distinct terms, preserving
    /// first-seen order. Substring matching, so an empty result means the query had no usable term.
    /// </summary>
    /// <param name="query">The raw query text.</param>
    /// <returns>The distinct, capped terms.</returns>
    public static IReadOnlyList<string> Tokenize(string query)
        => query is null
            ? []
            : query
                .Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
                .Take(VaultConfig.MaxQueryTerms)
                .Distinct(StringComparer.Ordinal)
                .ToList();

    /// <summary>
    /// Match one note body against <paramref name="terms"/>, returning the matched terms in
    /// first-occurrence order and up to <see cref="VaultConfig.MaxSnippetsPerNote"/> snippets. A
    /// note with no matched term yields an empty match; the caller drops it.
    /// </summary>
    /// <param name="candidate">The note being matched.</param>
    /// <param name="body">The note's decoded body.</param>
    /// <param name="terms">The tokenized query terms.</param>
    /// <returns>The match, possibly with no matched terms.</returns>
    public static ContentMatch MatchDocument(SearchCandidateRow candidate, string body, IReadOnlyList<string> terms)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(terms);

        var hits = terms
            .Select(term => (Term: term, Index: body.IndexOf(term, StringComparison.OrdinalIgnoreCase)))
            .Where(hit => hit.Index >= 0)
            .OrderBy(hit => hit.Index)
            .ThenBy(hit => hit.Term, StringComparer.Ordinal)
            .ToList();

        return new ContentMatch
        {
            Candidate = candidate,
            MatchedTerms = [.. hits.Select(hit => hit.Term)],
            Snippets = [.. hits.Take(VaultConfig.MaxSnippetsPerNote).Select(hit => BuildSnippet(body, hit.Term, hit.Index))],
        };
    }

    /// <summary>
    /// Select and rank matches per the all-terms-then-fallback rule. <paramref name="matched"/> must
    /// already exclude notes with no matched term. Ranking is distinct terms descending, then the
    /// shared list ordering.
    /// </summary>
    /// <param name="matched">The per-note matches (each with at least one matched term).</param>
    /// <param name="termCount">The number of query terms.</param>
    /// <returns>The ranked selection and the mode that produced it.</returns>
    public static ContentSearchResult SelectAndRank(IReadOnlyList<ContentMatch> matched, int termCount)
    {
        ArgumentNullException.ThrowIfNull(matched);

        var allTerms = matched.Where(match => match.MatchedTerms.Count == termCount).ToList();
        var (selected, mode) = allTerms.Count > 0 || termCount < 2
            ? (allTerms, ListMode.AllTerms)
            : (matched.ToList(), ListMode.AnyTermFallback);

        var ranked = selected
            .OrderByDescending(match => match.MatchedTerms.Count)
            .ThenBy(match => match.Candidate, CandidateComparer)
            .ToList();
        return new ContentSearchResult { Matches = ranked, Mode = mode };
    }

    /// <summary>Match every input in memory and rank the result; the all-in-memory convenience.</summary>
    /// <param name="inputs">The candidate notes with their decoded bodies.</param>
    /// <param name="query">The raw query text.</param>
    /// <returns>The ranked selection and the mode that produced it.</returns>
    public static ContentSearchResult Search(IReadOnlyList<SearchInput> inputs, string query)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var terms = Tokenize(query);
        var matched = inputs
            .Select(input => MatchDocument(input.Candidate, input.Body, terms))
            .Where(match => match.MatchedTerms.Count > 0)
            .ToList();
        return SelectAndRank(matched, terms.Count);
    }

    /// <summary>
    /// Build one windowed snippet around a term's occurrence: up to
    /// <see cref="VaultConfig.SnippetContextChars"/> code units each side, hard-capped at
    /// <see cref="VaultConfig.MaxSnippetChars"/>, with <c>…</c> markers where the body was cut and
    /// window edges snapped off surrogate pairs so no unpaired surrogate is ever emitted.
    /// </summary>
    private static ContentSnippet BuildSnippet(string body, string term, int matchIndex)
    {
        var matchEnd = matchIndex + term.Length;
        var start = Math.Max(0, matchIndex - VaultConfig.SnippetContextChars);
        var end = Math.Min(body.Length, matchEnd + VaultConfig.SnippetContextChars);
        if (end - start > VaultConfig.MaxSnippetChars)
        {
            end = start + VaultConfig.MaxSnippetChars;
        }

        start = SnapStart(body, start);
        end = SnapEnd(body, start, end);

        var prefix = start > 0 ? "…" : string.Empty;
        var suffix = end < body.Length ? "…" : string.Empty;
        return new ContentSnippet { Term = term, Text = prefix + body[start..end] + suffix };
    }

    /// <summary>Never begin a window on a low surrogate: it would orphan the preceding high half.</summary>
    private static int SnapStart(string body, int start)
        => start < body.Length && char.IsLowSurrogate(body[start]) ? start + 1 : start;

    /// <summary>
    /// Never end a window between a surrogate pair: an exclusive end on a low surrogate leaves its
    /// high half inside the window, so drop the high half too. Never cross below <paramref name="start"/>.
    /// </summary>
    private static int SnapEnd(string body, int start, int end)
        => end > start && end < body.Length && char.IsLowSurrogate(body[end]) ? end - 1 : end;
}