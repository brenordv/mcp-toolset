namespace RaccoonNinja.McpToolset.Files.Selection;

/// <summary>How a <see cref="FileSelector"/> chooses files: the one of glob, regex, or an explicit list that was supplied, or everything under the root when none was.</summary>
public enum SelectionMode
{
    /// <summary>No glob, regex, or paths were given: everything under the root, pruned by confinement, ignore, and the denylist.</summary>
    All,

    /// <summary>A glob pattern compiled to a regex and matched against each walked path.</summary>
    Glob,

    /// <summary>A raw regex matched against each walked path.</summary>
    Regex,

    /// <summary>An explicit list of paths, resolved through the read gate rather than enumerated.</summary>
    Paths,
}