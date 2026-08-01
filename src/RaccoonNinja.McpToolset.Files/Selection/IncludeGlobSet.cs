using System.Text.RegularExpressions;

namespace RaccoonNinja.McpToolset.Files.Selection;

/// <summary>
/// The re-include globs for one walk: paths an <c>include_ignored</c> request pulls back past the ignore
/// tiers for a single call. Each glob carries two things, both evaluated with the ignore layer's OS case
/// rules (case-sensitive on Linux, insensitive elsewhere) so a re-include lines up with what the ignore
/// tiers pruned: a compiled <see cref="Regex"/> that decides whether a file is re-included, and a literal
/// directory prefix that decides whether an ignored directory must be descended to reach a possible match.
/// It never re-includes a denylisted path; the walker runs the secret denylist before consulting this set.
/// </summary>
public sealed class IncludeGlobSet
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private static readonly char[] WildcardChars = ['*', '?', '[', '{'];

    private readonly IReadOnlyList<Regex> _matchers;
    private readonly IReadOnlyList<string> _descentPrefixes;

    private IncludeGlobSet(IReadOnlyList<Regex> matchers, IReadOnlyList<string> descentPrefixes)
    {
        _matchers = matchers;
        _descentPrefixes = descentPrefixes;
    }

    /// <summary>The empty set: nothing is re-included, so every ignore tier stays in force.</summary>
    public static IncludeGlobSet Empty { get; } = new([], []);

    /// <summary>Whether the set re-includes nothing (the common case).</summary>
    public bool IsEmpty => _matchers.Count == 0;

    /// <summary>
    /// Compile <paramref name="globs"/> into a re-include set. Blank entries are dropped; a <c>null</c> or
    /// all-blank list yields <see cref="Empty"/>. Matching always uses the ignore layer's OS case rules, not
    /// the selector's case sensitivity, because a re-include has to line up with what the ignore tiers pruned.
    /// </summary>
    /// <param name="globs">The raw re-include globs, or <c>null</c>.</param>
    /// <returns>The compiled set, or <see cref="Empty"/> when there is nothing to re-include.</returns>
    /// <exception cref="RegexCompilationException">Thrown when a glob is malformed.</exception>
    public static IncludeGlobSet Compile(IReadOnlyList<string> globs)
    {
        if (globs is null)
        {
            return Empty;
        }

        var matchers = new List<Regex>();
        var prefixes = new List<string>();
        foreach (var glob in globs)
        {
            if (string.IsNullOrWhiteSpace(glob))
            {
                continue;
            }

            var trimmed = glob.Trim();
            matchers.Add(GlobCompiler.Compile(trimmed, caseSensitive: OperatingSystem.IsLinux()));
            prefixes.Add(LiteralDirectoryPrefix(trimmed));
        }

        return matchers.Count == 0 ? Empty : new IncludeGlobSet(matchers, prefixes);
    }

    /// <summary>Whether <paramref name="relativePath"/> is re-included by any glob in the set.</summary>
    /// <param name="relativePath">The <c>/</c>-separated root-relative path.</param>
    /// <returns><c>true</c> when at least one glob matches.</returns>
    public bool Matches(string relativePath)
    {
        foreach (var matcher in _matchers)
        {
            if (matcher.IsMatch(relativePath))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether an ignored directory <paramref name="directoryRelativePath"/> could contain a re-included
    /// match and so must be descended. True when any glob's literal directory prefix is a path-prefix of the
    /// directory or the directory is a path-prefix of it; an empty prefix (from a basename glob or a
    /// root-<c>**</c> glob) relates to everything. A too-short prefix only over-descends, never dropping a
    /// match.
    /// </summary>
    /// <param name="directoryRelativePath">The <c>/</c>-separated root-relative directory path.</param>
    /// <returns><c>true</c> when the directory must be descended.</returns>
    public bool CouldContain(string directoryRelativePath)
    {
        foreach (var prefix in _descentPrefixes)
        {
            if (prefix.Length == 0
                || IsPathPrefixOrEqual(prefix, directoryRelativePath)
                || IsPathPrefixOrEqual(directoryRelativePath, prefix))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The literal directory prefix of a glob: its text up to the first wildcard, cut back to the last <c>/</c>.</summary>
    private static string LiteralDirectoryPrefix(string glob)
    {
        if (!glob.Contains('/'))
        {
            // A no-slash glob matches a basename at any depth, so it could match beneath any directory.
            return string.Empty;
        }

        var firstWildcard = glob.IndexOfAny(WildcardChars);
        var literal = firstWildcard < 0 ? glob : glob[..firstWildcard];
        var lastSlash = literal.LastIndexOf('/');
        return lastSlash < 0 ? string.Empty : literal[..lastSlash];
    }

    /// <summary>Whether <paramref name="inner"/> equals <paramref name="outer"/> or sits directly beneath it.</summary>
    private static bool IsPathPrefixOrEqual(string outer, string inner)
        => inner.Equals(outer, PathComparison)
           || inner.StartsWith(outer + "/", PathComparison);
}
