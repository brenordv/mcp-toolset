namespace RaccoonNinja.McpToolset.Server.GitOps.Security;

/// <summary>Which magic prefix <see cref="GitCommandBuilder"/> puts in front of a pathspec value.</summary>
public enum PathspecMagic
{
    /// <summary><c>:(literal)</c>: wildcards in the value are plain characters.</summary>
    Literal = 1,

    /// <summary>No prefix: git's default matching, where <c>*</c> and <c>?</c> also match <c>/</c>. Also used for a subcommand that takes a plain file argument rather than a pathspec (<c>git blame</c>).</summary>
    None = 2,

    /// <summary><c>:(exclude)</c>: paths matching the value are removed from the result. Accepted by <c>grep</c> and <c>ls-files</c>; <c>ls-tree</c> rejects it.</summary>
    Exclude = 3,
}