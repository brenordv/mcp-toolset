namespace RaccoonNinja.McpToolset.Server.TextSearch.Errors;

/// <summary>
/// The stable error-code taxonomy. Each code is its own name so it doubles as the log
/// <c>error_code</c>. Codes are the wire contract clients branch on, so their string values never
/// change once shipped.
/// </summary>
public static class ErrorCodes
{
    /// <summary>The selector was contradictory (more than one of glob, regex, paths).</summary>
    public const string SelectorInvalid = nameof(SelectorInvalid);

    /// <summary>The agent-supplied regex was too long, over the repetition cap, or not valid.</summary>
    public const string PatternInvalid = nameof(PatternInvalid);

    /// <summary>A supplied path or root escaped the confinement root.</summary>
    public const string PathOutsideRoot = nameof(PathOutsideRoot);

    /// <summary>A requested path did not exist, or resolved to a directory where a file was required.</summary>
    public const string NotFound = nameof(NotFound);

    /// <summary>The target file was binary and cannot be read as text.</summary>
    public const string IsBinary = nameof(IsBinary);

    /// <summary>The target file exceeded the configured size cap.</summary>
    public const string TooLarge = nameof(TooLarge);

    /// <summary>The target file could not be parsed as JSON.</summary>
    public const string JsonInvalid = nameof(JsonInvalid);

    /// <summary>A well-formed <c>json_path</c> did not resolve in the parsed document.</summary>
    public const string JsonPathNotFound = nameof(JsonPathNotFound);

    /// <summary>The extracted JSON value exceeded the configured value cap; a narrower <c>json_path</c> can retry.</summary>
    public const string ValueTooLarge = nameof(ValueTooLarge);

    /// <summary>The operation exceeded its wall-clock budget across the file set.</summary>
    public const string OperationBudgetExceeded = nameof(OperationBudgetExceeded);

    /// <summary>An argument was missing, malformed, or out of range.</summary>
    public const string InvalidArgument = nameof(InvalidArgument);

    /// <summary>The file's content matched a secret detector and was withheld.</summary>
    public const string WithheldSecret = nameof(WithheldSecret);

    /// <summary>An unexpected internal error; details go to the log, never the client.</summary>
    public const string InternalError = nameof(InternalError);
}