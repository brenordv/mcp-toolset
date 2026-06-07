namespace RaccoonNinja.McpToolset.Server.GitOps.Errors;

/// <summary>Stable error-code taxonomy emitted by the server (also used as log <c>error_code</c>).</summary>
public static class ErrorCodes
{
    public const string GitCheckError = nameof(GitCheckError);
    public const string GitNotInstalled = nameof(GitNotInstalled);
    public const string NotAGitRepository = nameof(NotAGitRepository);
    public const string RefNotFound = nameof(RefNotFound);
    public const string AmbiguousRef = nameof(AmbiguousRef);
    public const string PathNotFound = nameof(PathNotFound);
    public const string PathOutsideRepo = nameof(PathOutsideRepo);
    public const string RejectedArgument = nameof(RejectedArgument);
    public const string GitTimeout = nameof(GitTimeout);
    public const string GitCommandError = nameof(GitCommandError);
    public const string PcreUnavailable = nameof(PcreUnavailable);
}