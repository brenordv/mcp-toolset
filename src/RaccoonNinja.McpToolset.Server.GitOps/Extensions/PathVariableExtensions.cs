namespace RaccoonNinja.McpToolset.Server.GitOps.Extensions;

/// <summary>Reads a <c>PATH</c> value's entries the way the server trusts them.</summary>
internal static class PathVariableExtensions
{
    /// <summary>
    /// The fully qualified entries of <paramref name="pathVariable"/>, in order. Empty, relative, root-relative
    /// (<c>\tools</c> or <c>C:tools</c> on Windows), and quoted entries are skipped: none of them names a fixed directory.
    /// </summary>
    /// <param name="pathVariable">A <c>PATH</c> value, its entries separated by <see cref="Path.PathSeparator"/>.</param>
    /// <returns>The fully qualified entries, in their <c>PATH</c> order.</returns>
    public static IEnumerable<string> FullyQualifiedPathEntries(this string pathVariable)
        => pathVariable
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(Path.IsPathFullyQualified);
}