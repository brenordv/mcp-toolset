namespace RaccoonNinja.McpToolset.Common.Files.Selection;

/// <summary>
/// Decides whether a single explicitly named path is ignored, testing every ancestor directory the way
/// the pruning <see cref="FileWalker"/> does rather than only the leaf. The walker never descends an
/// ignored directory, so it only ever tests a leaf whose ancestors are already known to be un-ignored and
/// lets "everything under an ignored directory is ignored" fall out of pruning. A caller that names a path
/// directly, such as the write gate's <c>paths[]</c> selector, skips that traversal, so a <c>bin/</c> rule
/// that matches the directory would never match <c>bin/x.dll</c> on its own. This evaluator restores the
/// missing ancestor tests: it accumulates the project ignore-file chain (<c>.gitignore</c>, the agent-ignore files, <c>.mcpignore</c>) from the root down
/// and reports the path ignored when any ancestor directory or the leaf itself matches. An instance
/// remembers each directory's rule chain, for a caller that tests many paths under one root in a single
/// operation (a filtered walk). Like <see cref="IgnoreRules"/>, it is not the secret control; secrets are
/// guarded by <see cref="Security.SecretDenylist"/> and the content scan.
/// </summary>
public sealed class PathIgnoreEvaluator
{
    private readonly string _rootDirectory;
    private readonly Dictionary<string, DirectoryState> _directories = new(StringComparer.Ordinal);
    private DirectoryState _rootState;

    /// <summary>
    /// Create an evaluator for one root that remembers the rule chain of every directory it has resolved,
    /// so testing many paths under the same root costs one ignore-file load per directory rather than one
    /// per path. Ignore files are read once, on first use of each directory, so create one instance per
    /// operation; it does not see ignore files that change while it is alive. Not thread-safe.
    /// </summary>
    /// <param name="rootDirectory">The real absolute root directory paths are anchored under.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="rootDirectory"/> is null or blank.</exception>
    public PathIgnoreEvaluator(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = rootDirectory;
    }

    /// <summary>
    /// Whether <paramref name="relativePath"/> is ignored under the ignore files reachable from
    /// <paramref name="rootDirectory"/>, testing each ancestor directory as a directory and then the leaf
    /// as a file. Rules accumulate root-first and are ordered by <see cref="IgnoreRules.Combine"/>, exactly
    /// as the walker composes them while descending.
    /// </summary>
    /// <param name="rootDirectory">The real absolute root directory the relative path is anchored under.</param>
    /// <param name="relativePath">The <c>/</c>-separated root-relative path of the file to test.</param>
    /// <returns><c>true</c> when any ancestor directory or the leaf is ignored; otherwise <c>false</c>.</returns>
    /// <exception cref="ArgumentException">Thrown when either argument is null or blank.</exception>
    public static bool IsIgnored(string rootDirectory, string relativePath)
        => new PathIgnoreEvaluator(rootDirectory).IsIgnored(relativePath);

    /// <summary>
    /// Whether <paramref name="relativePath"/> is ignored under this evaluator's root, with the same answer
    /// as <see cref="IsIgnored(string, string)"/>: each ancestor directory is tested as a directory, then
    /// the leaf as a file.
    /// </summary>
    /// <param name="relativePath">The <c>/</c>-separated root-relative path of the file to test.</param>
    /// <returns><c>true</c> when any ancestor directory or the leaf is ignored; otherwise <c>false</c>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="relativePath"/> is null or blank.</exception>
    public bool IsIgnored(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        if (relativePath == ".")
        {
            return false;
        }

        var segments = relativePath.Split('/');
        var state = _rootState ??= new DirectoryState(_rootDirectory, IgnoreRules.Load(_rootDirectory, string.Empty), Ignored: false);
        var relSoFar = string.Empty;

        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (segment.Length == 0)
            {
                continue;
            }

            relSoFar = relSoFar.Length == 0 ? segment : string.Concat(relSoFar, "/", segment);
            if (i == segments.Length - 1)
            {
                return state.Rules.IsIgnored(relSoFar, isDirectory: false);
            }

            state = Descend(state, relSoFar, segment);
            if (state.Ignored)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Resolve, or recall, the state of the child directory <paramref name="segment"/> under <paramref name="parent"/>.</summary>
    private DirectoryState Descend(DirectoryState parent, string relativeDirectory, string segment)
    {
        if (_directories.TryGetValue(relativeDirectory, out var known))
        {
            return known;
        }

        var realPath = Path.Combine(parent.RealPath, segment);
        DirectoryState state;
        if (parent.Rules.IsIgnored(relativeDirectory, isDirectory: true))
        {
            state = new DirectoryState(realPath, parent.Rules, Ignored: true);
        }
        else
        {
            var own = IgnoreRules.Load(realPath, relativeDirectory);
            var rules = ReferenceEquals(own, IgnoreRules.Empty) ? parent.Rules : IgnoreRules.Combine([parent.Rules, own]);
            state = new DirectoryState(realPath, rules, Ignored: false);
        }

        _directories[relativeDirectory] = state;
        return state;
    }

    /// <summary>One resolved directory: its real path, the rules in force inside it, and whether it is itself ignored.</summary>
    private sealed record DirectoryState(string RealPath, IgnoreRules Rules, bool Ignored);
}