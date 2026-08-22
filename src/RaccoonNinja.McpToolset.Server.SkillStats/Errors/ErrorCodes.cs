namespace RaccoonNinja.McpToolset.Server.SkillStats.Errors;

/// <summary>
/// The stable error-code taxonomy. Each code doubles as the log <c>error_code</c>. Codes are the wire
/// contract clients branch on, so their string values never change once shipped.
/// </summary>
public static class ErrorCodes
{
    /// <summary>An argument was missing, malformed, or out of range.</summary>
    public const string InvalidArgument = nameof(InvalidArgument);

    /// <summary>The skill-usage database is absent, unreadable, or of an incompatible schema.</summary>
    public const string StoreUnavailable = nameof(StoreUnavailable);

    /// <summary>An unexpected internal error; details go to the log, never the client.</summary>
    public const string InternalError = nameof(InternalError);
}