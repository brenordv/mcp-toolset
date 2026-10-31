namespace RaccoonNinja.McpToolset.Common.Files.Selection;

/// <summary>
/// An ordered set of project ignore rules (<c>.gitignore</c>, the AI-agent ignore files, and
/// <c>.mcpignore</c>) evaluated with git's last-match-wins semantics: the last rule that matches
/// a path decides, and a <c>!</c> rule re-includes what an earlier rule excluded. Rules are
/// ordered tier by tier (the built-in default, then <c>.gitignore</c>, then the agent-ignore
/// files, then <c>.mcpignore</c>) and root-down within each tier, so a nested <c>.gitignore</c>
/// cannot re-include what a shallower <c>.mcpignore</c> hides. Matching is per-entry, which is
/// all the pruning walker needs: it never descends an ignored directory, so a path is only ever
/// tested once its ancestors are known to be un-ignored. Every tool honors these rules, but they
/// are not the secret control: ignore files are ordinary editable files, so secrets are guarded
/// by <see cref="Security.SecretDenylist"/> and the content scan, which no ignore rule can
/// override. Which ignore files a directory holds is read from its listing, so a directory the
/// server cannot list fails the operation rather than reading as rule-free.
/// </summary>
public sealed class IgnoreRules
{
    /// <summary>The git ignore-file name read at each directory level.</summary>
    public const string GitIgnoreFileName = ".gitignore";

    /// <summary>The toolset-specific ignore-file name, applied last so it overrides <c>.gitignore</c> and the agent ignore files.</summary>
    public const string McpIgnoreFileName = ".mcpignore";

    /// <summary>
    /// The AI-agent context-exclusion ignore files honored alongside git's, applied after <c>.gitignore</c>
    /// and before <c>.mcpignore</c>. Curated to files whose intent is that an agent should not surface the
    /// listed paths; index-only files such as <c>.cursorindexingignore</c> and tool-scope ignores such as
    /// <c>.npmignore</c> or <c>.dockerignore</c> are deliberately excluded.
    /// </summary>
    public static IReadOnlyList<string> AgentIgnoreFileNames { get; } =
    [
        ".claudeignore", ".cursorignore", ".aiexclude", ".aiignore",
        ".codeiumignore", ".continueignore", ".aiderignore", ".geminiignore",
    ];

    /// <summary>
    /// Every project ignore-file kind honored at a directory level, least specific first: <c>.gitignore</c>,
    /// then the agent ignore files, then <c>.mcpignore</c> last so it overrides the rest.
    /// </summary>
    public static IReadOnlyList<string> IgnoreFileNames { get; } =
        [GitIgnoreFileName, .. AgentIgnoreFileNames, McpIgnoreFileName];

    // File names come from a directory listing, so matching is case-insensitive on every platform, the same
    // rule IsIgnoreFileName applies on the write side.
    private static readonly StringComparer IgnoreNameComparer = StringComparer.OrdinalIgnoreCase;

    private readonly IReadOnlyList<IgnoreRule> _rules;

    private IgnoreRules(IReadOnlyList<IgnoreRule> rules) => _rules = rules;

    /// <summary>A rule set that ignores nothing.</summary>
    public static IgnoreRules Empty { get; } = new([]);

    /// <summary>The source pattern lines that produced rules, in order, for <c>describe_scope</c> reporting.</summary>
    public IReadOnlyList<string> Patterns => _rules.Select(rule => rule.Source).ToArray();

    /// <summary>
    /// Whether the last segment of <paramref name="relativePath"/> is one of <see cref="IgnoreFileNames"/>.
    /// The comparison is ordinal and ignores case, as the secret denylist's name matching does.
    /// </summary>
    /// <param name="relativePath">A <c>/</c>-separated root-relative path.</param>
    /// <returns><c>true</c> when the path names an ignore file at any depth; otherwise <c>false</c>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="relativePath"/> is null or blank.</exception>
    public static bool IsIgnoreFileName(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var fileName = relativePath[(relativePath.LastIndexOf('/') + 1)..];
        return IgnoreFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Compile ignore-file <paramref name="lines"/> into a rule set anchored under <paramref name="basePath"/>,
    /// in the built-in default tier (the least specific). Blank lines and comments contribute nothing.
    /// Project ignore files are read through <see cref="Load"/>, which assigns their own tiers.
    /// </summary>
    /// <param name="lines">The raw lines of an ignore file, in order.</param>
    /// <param name="basePath">The root-relative POSIX directory the ignore file lives in (<c>""</c> for the root).</param>
    /// <returns>The compiled rule set.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="lines"/> is <c>null</c>.</exception>
    public static IgnoreRules Parse(IEnumerable<string> lines, string basePath = "")
    {
        ArgumentNullException.ThrowIfNull(lines);

        return Parse(lines, basePath, IgnoreTier.BuiltInDefault);
    }

    /// <summary>Compile <paramref name="lines"/> into a rule set of the given <paramref name="tier"/>, anchored under <paramref name="basePath"/>.</summary>
    private static IgnoreRules Parse(IEnumerable<string> lines, string basePath, IgnoreTier tier)
    {
        var rules = new List<IgnoreRule>();
        foreach (var line in lines)
        {
            var rule = IgnoreRule.TryCompile(line, basePath, tier);
            if (rule is not null)
            {
                rules.Add(rule);
            }
        }

        return new IgnoreRules(rules);
    }

    /// <summary>
    /// Read every ignore-file kind in <see cref="IgnoreFileNames"/> present in <paramref name="directory"/> and
    /// compile them into one rule set anchored under <paramref name="basePath"/>. Which kinds are present comes
    /// from one listing of the directory, so an absent kind costs no probe, a directory that does not exist holds
    /// none, and a directory that exists but cannot be listed fails loudly rather than reading as empty. The kinds
    /// are read least-specific first, so <c>.mcpignore</c> rules come last and override the rest.
    /// </summary>
    /// <param name="directory">The real absolute directory to read the ignore files from.</param>
    /// <param name="basePath">The root-relative POSIX path of that directory (<c>""</c> for the root).</param>
    /// <returns>The combined rule set, or <see cref="Empty"/> when no ignore file is present or the directory does not exist.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="directory"/> is null or blank.</exception>
    /// <exception cref="IgnoreDirectoryUnlistableException">Thrown when the directory exists but cannot be listed; the message carries only its root-relative path.</exception>
    /// <exception cref="IgnoreFileReadException">Thrown when a listed ignore file cannot be read; the message carries only its root-relative path.</exception>
    public static IgnoreRules Load(string directory, string basePath = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var present = ListIgnoreFiles(directory, basePath);
        var sets = new List<IgnoreRules>();
        foreach (var fileName in IgnoreFileNames)
        {
            if (!present.TryGetValue(fileName, out var paths))
            {
                continue;
            }

            var tier = fileName switch
            {
                GitIgnoreFileName => IgnoreTier.GitIgnore,
                McpIgnoreFileName => IgnoreTier.McpIgnore,
                _ => IgnoreTier.AgentIgnore,
            };

            // On a case-sensitive filesystem one kind can be present under several spellings; every one
            // loads, in ordinal order so the result is deterministic, and none shadows another.
            paths.Sort(StringComparer.Ordinal);
            foreach (var path in paths)
            {
                var rules = ReadFile(path, Path.GetFileName(path), basePath, tier);
                if (rules is not null)
                {
                    sets.Add(rules);
                }
            }
        }

        return sets.Count == 0 ? Empty : Combine(sets);
    }

    /// <summary>
    /// Merge rule sets for evaluation. Rules are ordered by tier, least specific first, so a more specific
    /// tier (<c>.mcpignore</c> over the agent-ignore files over <c>.gitignore</c> over the built-in default)
    /// wins regardless of directory depth. Within one tier the input order is kept, so a set passed later
    /// (a deeper ignore file) overrides an earlier one, as in git.
    /// </summary>
    /// <param name="sets">The rule sets, root first.</param>
    /// <returns>The combined rule set.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sets"/> is <c>null</c>.</exception>
    public static IgnoreRules Combine(IEnumerable<IgnoreRules> sets)
    {
        ArgumentNullException.ThrowIfNull(sets);

        var combined = sets
            .SelectMany(set => set._rules)
            .OrderBy(rule => rule.Tier)
            .ToArray();
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

    /// <summary>List the ignore files in <paramref name="directory"/>, grouped by kind under <see cref="IgnoreNameComparer"/>; empty when the directory does not exist.</summary>
    /// <exception cref="IgnoreDirectoryUnlistableException">Thrown when the directory exists but cannot be listed.</exception>
    private static Dictionary<string, List<string>> ListIgnoreFiles(string directory, string basePath)
    {
        var present = new Dictionary<string, List<string>>(IgnoreNameComparer);
        try
        {
            // The single-argument overload throws on a directory the caller cannot read and lists hidden
            // files; the EnumerationOptions overload defaults to skipping both, which would read a denied
            // directory as rule-free.
            foreach (var path in Directory.EnumerateFiles(directory))
            {
                var name = Path.GetFileName(path);
                if (!IgnoreFileNames.Contains(name, IgnoreNameComparer))
                {
                    continue;
                }

                if (!present.TryGetValue(name, out var paths))
                {
                    paths = [];
                    present[name] = paths;
                }

                paths.Add(path);
            }
        }
        catch (DirectoryNotFoundException)
        {
            // A directory that does not exist holds no ignore files, and nothing beneath it can be opened.
            return present;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var relativeDirectory = string.IsNullOrWhiteSpace(basePath) ? "." : basePath;
            throw new IgnoreDirectoryUnlistableException(relativeDirectory, ex.GetType().Name);
        }

        return present;
    }

    /// <summary>Read and compile one listed ignore file; <c>null</c> when it vanished between the listing and the read or is a dangling link.</summary>
    /// <exception cref="IgnoreFileReadException">Thrown when the file exists but cannot be read.</exception>
    private static IgnoreRules ReadFile(string path, string fileName, string basePath, IgnoreTier tier)
    {
        try
        {
            // Parse enumerates the lines, so a read that fails partway through also lands in the catch.
            return Parse(File.ReadLines(path), basePath, tier);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var relativePath = string.IsNullOrWhiteSpace(basePath) ? fileName : string.Concat(basePath, "/", fileName);
            throw new IgnoreFileReadException(relativePath, ex.GetType().Name);
        }
    }
}