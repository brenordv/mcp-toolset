namespace RaccoonNinja.McpToolset.Files.Security;

/// <summary>
/// Confines agent-supplied paths to a single fixed root. The root comes from server configuration
/// (the <c>--roots</c> boundary), never from the agent, so a caller can narrow to a path inside the
/// root but can never widen past it. This is the load-bearing security control both servers depend on:
/// it resolves every symbolic link and junction in the chain and refuses anything whose real target
/// escapes the root, so once the read tools are blanket-approved a path can no longer reach outside.
/// </summary>
public interface IRootResolver
{
    /// <summary>The canonical, fully-resolved absolute root, with no trailing separator. Machine-identifying; never surface it.</summary>
    string CanonicalRoot { get; }

    /// <summary>
    /// Confine <paramref name="candidate"/> under the root and return its resolved form. The candidate may
    /// be relative to the root or an absolute path already inside it; either way it is rejected unless its
    /// real target, after full symlink and junction resolution, sits inside the root.
    /// </summary>
    /// <param name="candidate">The path to confine, from the agent or a caller.</param>
    /// <param name="paramName">The tool parameter name to name in a refusal (defaults to <c>path</c>).</param>
    /// <returns>The confined path, carrying both its real absolute location and its root-relative POSIX form.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="candidate"/> is <c>null</c>.</exception>
    /// <exception cref="PathConfinementException">Thrown when the path is syntactically hostile, malformed, or escapes the root.</exception>
    ConfinedPath Confine(string candidate, string paramName = "path");
}