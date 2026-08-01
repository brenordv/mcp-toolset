namespace RaccoonNinja.McpToolset.Files.Selection;

/// <summary>
/// Thrown when a selection request is contradictory, above all when more than one of glob, regex, or paths
/// is supplied. The message is loud and plain so a server maps it into a typed tool error the agent can
/// recover from, rather than silently picking one and returning quietly wrong results.
/// </summary>
public sealed class SelectorException : Exception
{
    /// <summary>Create the exception with a caller-facing <paramref name="reason"/>.</summary>
    /// <param name="reason">Why the selector was rejected.</param>
    public SelectorException(string reason)
        : base(reason)
    {
    }
}