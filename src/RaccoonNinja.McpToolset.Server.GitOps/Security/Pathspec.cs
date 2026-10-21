namespace RaccoonNinja.McpToolset.Server.GitOps.Security;

/// <summary>One pathspec to emit after <c>--</c>: a user-supplied value, the server-chosen magic that prefixes it, and the parameter name the masked argv log shows in place of the value.</summary>
/// <param name="Magic">The prefix the builder renders.</param>
/// <param name="Value">The confined path or validated pattern.</param>
/// <param name="Param">The tool parameter the value came from (<c>path</c>, <c>glob</c>, or <c>exclude</c>).</param>
public readonly record struct Pathspec(PathspecMagic Magic, string Value, string Param)
{
    /// <summary>A confined repo-relative path, rendered as <c>:(literal)value</c>.</summary>
    /// <param name="value">The confined path.</param>
    /// <returns>A literal pathspec attributed to <c>path</c>.</returns>
    public static Pathspec Literal(string value) => new(PathspecMagic.Literal, value, "path");

    /// <summary>A validated wildcard pattern, rendered verbatim (git's default matching).</summary>
    /// <param name="value">The validated pattern.</param>
    /// <returns>A bare pathspec attributed to <c>glob</c>.</returns>
    public static Pathspec Glob(string value) => new(PathspecMagic.None, value, "glob");

    /// <summary>A validated path or pattern rendered as <c>:(exclude)value</c>.</summary>
    /// <param name="value">The validated path or pattern to exclude.</param>
    /// <returns>An exclude pathspec attributed to <c>exclude</c>.</returns>
    public static Pathspec Exclude(string value) => new(PathspecMagic.Exclude, value, "exclude");

    /// <summary>A confined file path rendered verbatim, for <c>git blame</c>, which takes a file argument rather than a pathspec.</summary>
    /// <param name="value">The confined file path.</param>
    /// <returns>A bare file argument attributed to <c>path</c>.</returns>
    public static Pathspec FileArgument(string value) => new(PathspecMagic.None, value, "path");
}