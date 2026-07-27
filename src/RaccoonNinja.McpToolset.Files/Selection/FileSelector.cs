namespace RaccoonNinja.McpToolset.Files.Selection;

/// <summary>
/// A validated request for which files a multi-file tool should act on. Exactly one of glob, regex, or an
/// explicit path list picks the files; supplying more than one is a loud error, and supplying none means
/// "everything under the root". The three modifiers (<see cref="IncludeIgnored"/>, <see cref="CaseSensitive"/>,
/// <see cref="MaxFiles"/>) compose with whichever was chosen. Building one through <see cref="Create"/> is the
/// only way to get an instance, so an invalid combination cannot reach the walker.
/// </summary>
public sealed class FileSelector
{
    private FileSelector(
        SelectionMode mode,
        string root,
        string glob,
        string regex,
        IReadOnlyList<string> paths,
        bool includeIgnored,
        bool caseSensitive,
        int maxFiles)
    {
        Mode = mode;
        Root = root;
        Glob = glob;
        Regex = regex;
        Paths = paths;
        IncludeIgnored = includeIgnored;
        CaseSensitive = caseSensitive;
        MaxFiles = maxFiles;
    }

    /// <summary>Which of glob, regex, paths, or "everything" this selector uses.</summary>
    public SelectionMode Mode { get; }

    /// <summary>The root-relative directory to scope to; <c>null</c> means the whole root.</summary>
    public string Root { get; }

    /// <summary>The glob pattern when <see cref="Mode"/> is <see cref="SelectionMode.Glob"/>; otherwise <c>null</c>.</summary>
    public string Glob { get; }

    /// <summary>The raw regex when <see cref="Mode"/> is <see cref="SelectionMode.Regex"/>; otherwise <c>null</c>.</summary>
    public string Regex { get; }

    /// <summary>The explicit paths when <see cref="Mode"/> is <see cref="SelectionMode.Paths"/>; otherwise <c>null</c>.</summary>
    public IReadOnlyList<string> Paths { get; }

    /// <summary>Whether ignore-file rules are bypassed (text-search only; the server rejects it for text-edit).</summary>
    public bool IncludeIgnored { get; }

    /// <summary>Whether matching is case-sensitive; never propagated to the denylist, which is always insensitive.</summary>
    public bool CaseSensitive { get; }

    /// <summary>The maximum number of files to return; the server clamps this to its configured ceiling.</summary>
    public int MaxFiles { get; }

    /// <summary>
    /// Build a validated selector, enforcing that at most one of <paramref name="glob"/>, <paramref name="regex"/>,
    /// or <paramref name="paths"/> is supplied. A blank glob or regex counts as absent; a non-null
    /// <paramref name="paths"/> (even empty) counts as present so <c>paths: []</c> selects nothing rather than
    /// everything.
    /// </summary>
    /// <param name="root">The root-relative directory to scope to, or <c>null</c> for the whole root.</param>
    /// <param name="glob">A glob pattern, or <c>null</c>.</param>
    /// <param name="regex">A raw regex, or <c>null</c>.</param>
    /// <param name="paths">An explicit path list, or <c>null</c>.</param>
    /// <param name="includeIgnored">Whether to bypass ignore rules.</param>
    /// <param name="caseSensitive">Whether matching is case-sensitive.</param>
    /// <param name="maxFiles">The result cap; defaults to unbounded so the server sets the policy ceiling.</param>
    /// <returns>The validated selector.</returns>
    /// <exception cref="SelectorException">Thrown when more than one of glob, regex, or paths is supplied.</exception>
    public static FileSelector Create(
        string root = null,
        string glob = null,
        string regex = null,
        IReadOnlyList<string> paths = null,
        bool includeIgnored = false,
        bool caseSensitive = false,
        int maxFiles = int.MaxValue)
    {
        var hasGlob = !string.IsNullOrWhiteSpace(glob);
        var hasRegex = !string.IsNullOrWhiteSpace(regex);
        var hasPaths = paths is not null;

        var chosen = (hasGlob ? 1 : 0) + (hasRegex ? 1 : 0) + (hasPaths ? 1 : 0);
        if (chosen > 1)
        {
            throw new SelectorException("provide exactly one of glob, regex, paths");
        }

        var mode = hasGlob ? SelectionMode.Glob
            : hasRegex ? SelectionMode.Regex
            : hasPaths ? SelectionMode.Paths
            : SelectionMode.All;

        return new FileSelector(
            mode,
            string.IsNullOrWhiteSpace(root) ? null : root,
            hasGlob ? glob : null,
            hasRegex ? regex : null,
            hasPaths ? paths : null,
            includeIgnored,
            caseSensitive,
            maxFiles);
    }
}