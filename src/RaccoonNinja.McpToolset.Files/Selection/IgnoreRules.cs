namespace RaccoonNinja.McpToolset.Files.Selection;

/// <summary>
/// An ordered set of <c>.gitignore</c>/<c>.mcpignore</c> rules evaluated with git's last-match-wins
/// semantics: the last rule that matches a path decides, and a <c>!</c> rule re-includes what an earlier
/// rule excluded. Matching is per-entry, which is all the pruning walker needs: it never descends an
/// ignored directory, so a path is only ever tested once its ancestors are known to be un-ignored, and
/// "everything under an ignored directory is ignored" falls out of pruning rather than descendant
/// matching. Ignore is a convenience rail against clobbering <c>bin/</c>, <c>obj/</c>, and the like; it
/// is not a security boundary (an agent can edit <c>.gitignore</c>). The boundary is
/// <see cref="Security.SecretDenylist"/>.
/// </summary>
public sealed class IgnoreRules
{
    /// <summary>The git ignore-file name read at each directory level.</summary>
    public const string GitIgnoreFileName = ".gitignore";

    /// <summary>The toolset-specific ignore-file name, applied after (so it overrides) <c>.gitignore</c>.</summary>
    public const string McpIgnoreFileName = ".mcpignore";

    private readonly IReadOnlyList<IgnoreRule> _rules;

    private IgnoreRules(IReadOnlyList<IgnoreRule> rules) => _rules = rules;

    /// <summary>A rule set that ignores nothing.</summary>
    public static IgnoreRules Empty { get; } = new([]);

    /// <summary>The source pattern lines that produced rules, in order, for <c>describe_scope</c> reporting.</summary>
    public IReadOnlyList<string> Patterns => _rules.Select(rule => rule.Source).ToArray();

    /// <summary>
    /// Compile ignore-file <paramref name="lines"/> into a rule set anchored under <paramref name="basePath"/>.
    /// Blank lines and comments contribute nothing.
    /// </summary>
    /// <param name="lines">The raw lines of an ignore file, in order.</param>
    /// <param name="basePath">The root-relative POSIX directory the ignore file lives in (<c>""</c> for the root).</param>
    /// <returns>The compiled rule set.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="lines"/> is <c>null</c>.</exception>
    public static IgnoreRules Parse(IEnumerable<string> lines, string basePath = "")
    {
        ArgumentNullException.ThrowIfNull(lines);

        var rules = new List<IgnoreRule>();
        foreach (var line in lines)
        {
            var rule = IgnoreRule.TryCompile(line, basePath ?? string.Empty);
            if (rule is not null)
            {
                rules.Add(rule);
            }
        }

        return new IgnoreRules(rules);
    }

    /// <summary>
    /// Read <c>.gitignore</c> then <c>.mcpignore</c> from <paramref name="directory"/> and compile them into
    /// one rule set anchored under <paramref name="basePath"/>. Absent files are skipped; <c>.mcpignore</c>
    /// rules come last so they override <c>.gitignore</c>.
    /// </summary>
    /// <param name="directory">The real absolute directory to read the ignore files from.</param>
    /// <param name="basePath">The root-relative POSIX path of that directory (<c>""</c> for the root).</param>
    /// <returns>The combined rule set, or <see cref="Empty"/> when neither file is present.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="directory"/> is null or blank.</exception>
    public static IgnoreRules Load(string directory, string basePath = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var sets = new List<IgnoreRules>();
        AddFileIfPresent(sets, Path.Combine(directory, GitIgnoreFileName), basePath ?? string.Empty);
        AddFileIfPresent(sets, Path.Combine(directory, McpIgnoreFileName), basePath ?? string.Empty);
        return sets.Count == 0 ? Empty : Combine(sets);
    }

    /// <summary>
    /// Concatenate rule sets so later ones take precedence (deeper ignore files win over shallower ones,
    /// matching git). The first set is the least specific.
    /// </summary>
    /// <param name="sets">The rule sets, least-specific first.</param>
    /// <returns>The combined rule set.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sets"/> is <c>null</c>.</exception>
    public static IgnoreRules Combine(IEnumerable<IgnoreRules> sets)
    {
        ArgumentNullException.ThrowIfNull(sets);

        var combined = new List<IgnoreRule>();
        foreach (var set in sets)
        {
            combined.AddRange(set._rules);
        }

        return new IgnoreRules(combined);
    }

    /// <summary>
    /// Whether <paramref name="relativePath"/> is ignored, applying last-match-wins across the rules.
    /// Assumes the entry's ancestors were not pruned (the walker's invariant), so it tests only this path.
    /// </summary>
    /// <param name="relativePath">The <c>/</c>-separated root-relative path of the entry.</param>
    /// <param name="isDirectory">Whether the entry is a directory (a directory-only rule needs this).</param>
    /// <returns><c>true</c> when the last matching rule ignores the path; <c>false</c> when none match or the last re-includes.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="relativePath"/> is null or blank.</exception>
    public bool IsIgnored(string relativePath, bool isDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var ignored = false;
        foreach (var rule in _rules)
        {
            if (rule.IsMatch(relativePath, isDirectory))
            {
                ignored = !rule.Negated;
            }
        }

        return ignored;
    }

    private static void AddFileIfPresent(List<IgnoreRules> sets, string path, string basePath)
    {
        if (File.Exists(path))
        {
            sets.Add(Parse(File.ReadLines(path), basePath));
        }
    }
}