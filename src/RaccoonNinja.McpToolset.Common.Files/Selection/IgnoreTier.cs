namespace RaccoonNinja.McpToolset.Common.Files.Selection;

/// <summary>
/// Which ignore tier a rule came from. Higher values are more specific and win over lower ones regardless
/// of directory depth.
/// </summary>
internal enum IgnoreTier
{
    /// <summary>The server's built-in or operator-supplied default ignore set.</summary>
    BuiltInDefault = 1,

    /// <summary>A <c>.gitignore</c> file.</summary>
    GitIgnore = 2,

    /// <summary>One of the AI-agent ignore files in <see cref="IgnoreRules.AgentIgnoreFileNames"/>.</summary>
    AgentIgnore = 3,

    /// <summary>A <c>.mcpignore</c> file.</summary>
    McpIgnore = 4,
}