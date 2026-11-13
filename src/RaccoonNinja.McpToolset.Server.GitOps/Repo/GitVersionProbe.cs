using RaccoonNinja.McpToolset.Server.GitOps.Errors;

namespace RaccoonNinja.McpToolset.Server.GitOps.Repo;

/// <summary>
/// What the one <c>git version</c> call at startup found: the version, or the code naming why there is none. The
/// <c>git_version</c> tool returns it, so no tool call starts <c>git version</c> again.
/// </summary>
public sealed record GitVersionProbe
{
    /// <summary>
    /// The text after <c>git version </c> in git's output, allowlisted by <see cref="GitVersionReader.ParseVersion"/>;
    /// <c>null</c> when none was read.
    /// </summary>
    public string Version { get; init; }

    /// <summary>
    /// Why <see cref="Version"/> is <c>null</c>: <see cref="ErrorCodes.GitNotInstalled"/>,
    /// <see cref="ErrorCodes.GitTimeout"/>, or <see cref="ErrorCodes.GitCommandError"/>; <c>null</c> when a version was
    /// read.
    /// </summary>
    public string ErrorCode { get; init; }

    /// <summary>
    /// git's exit code when git exited without giving a version (0 when it printed no line the server accepts);
    /// <c>null</c> when it never started or was killed.
    /// </summary>
    public int? GitExitCode { get; init; }
}