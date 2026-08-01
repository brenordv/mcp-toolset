namespace RaccoonNinja.McpToolset.Files.Selection;

/// <summary>
/// The knobs for a single <see cref="FileWalker"/> run. Defaults describe the common "everything under
/// the root, files only" walk; a caller narrows it with a starting subdirectory, a match predicate, and
/// the two safety caps.
/// </summary>
public sealed record FileWalkOptions
{
    /// <summary>The root-relative directory to start from; <c>.</c> (the default) walks the whole root.</summary>
    public string Start { get; init; } = ".";

    /// <summary>
    /// A predicate over an entry's root-relative path deciding whether it is returned. <c>null</c> (the
    /// default) returns every entry that survives pruning. Pruning always runs first, so the predicate
    /// never sees a denylisted, ignored, or symlinked path.
    /// </summary>
    public Func<string, bool> Match { get; init; }

    /// <summary>
    /// The globs that re-include otherwise-ignored paths for this walk (text-search only; empty by default,
    /// so every ignore tier stays in force). Never re-includes a denylisted path.
    /// </summary>
    public IncludeGlobSet IncludeIgnored { get; init; } = IncludeGlobSet.Empty;

    /// <summary>When <c>true</c>, directories that survive pruning are returned too; by default only files are.</summary>
    public bool IncludeDirectories { get; init; }

    /// <summary>The maximum number of entries to return; the result is sorted before this cap is applied.</summary>
    public int MaxResults { get; init; } = int.MaxValue;

    /// <summary>A backstop on the number of filesystem entries examined, bounding a pathological tree.</summary>
    public int MaxVisitedNodes { get; init; } = 1_000_000;
}