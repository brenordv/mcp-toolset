namespace RaccoonNinja.McpToolset.Files.Selection;

/// <summary>
/// Decides whether a single explicitly named path is ignored, testing every ancestor directory the way
/// the pruning <see cref="FileWalker"/> does rather than only the leaf. The walker never descends an
/// ignored directory, so it only ever tests a leaf whose ancestors are already known to be un-ignored and
/// lets "everything under an ignored directory is ignored" fall out of pruning. A caller that names a path
/// directly, such as the write gate's <c>paths[]</c> selector, skips that traversal, so a <c>bin/</c> rule
/// that matches the directory would never match <c>bin/x.dll</c> on its own. This evaluator restores the
/// missing ancestor tests: it accumulates the project ignore-file chain (<c>.gitignore</c>, the agent-ignore files, <c>.mcpignore</c>) from the root down
/// and reports the path ignored when any ancestor directory or the leaf itself matches. Like
/// <see cref="IgnoreRules"/>, this is a convenience rail against clobbering generated trees, not a security
/// boundary; the boundary is <see cref="Security.SecretDenylist"/>.
/// </summary>
public static class PathIgnoreEvaluator
{
    /// <summary>
    /// Whether <paramref name="relativePath"/> is ignored under the ignore files reachable from
    /// <paramref name="rootDirectory"/>, testing each ancestor directory as a directory and then the leaf
    /// as a file. Rules accumulate root-first so a deeper ignore file overrides a shallower one, exactly as
    /// the walker composes them while descending.
    /// </summary>
    /// <param name="rootDirectory">The real absolute root directory the relative path is anchored under.</param>
    /// <param name="relativePath">The <c>/</c>-separated root-relative path of the file to test.</param>
    /// <returns><c>true</c> when any ancestor directory or the leaf is ignored; otherwise <c>false</c>.</returns>
    /// <exception cref="ArgumentException">Thrown when either argument is null or blank.</exception>
    public static bool IsIgnored(string rootDirectory, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        if (relativePath == ".")
        {
            return false;
        }

        var segments = relativePath.Split('/');
        var rules = IgnoreRules.Load(rootDirectory, string.Empty);
        var realSoFar = rootDirectory;
        var relSoFar = string.Empty;

        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (segment.Length == 0)
            {
                continue;
            }

            relSoFar = relSoFar.Length == 0 ? segment : string.Concat(relSoFar, "/", segment);
            var isDirectory = i < segments.Length - 1;

            if (rules.IsIgnored(relSoFar, isDirectory))
            {
                return true;
            }

            if (isDirectory)
            {
                realSoFar = Path.Combine(realSoFar, segment);
                rules = IgnoreRules.Combine([rules, IgnoreRules.Load(realSoFar, relSoFar)]);
            }
        }

        return false;
    }
}