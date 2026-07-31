namespace RaccoonNinja.McpToolset.Server.TextEdit.Errors;

/// <summary>
/// The stable error-code taxonomy. Each code is its own name so it doubles as the log
/// <c>error_code</c>. Codes are the wire contract clients branch on, so their string values never
/// change once shipped. The first group mirrors the read-side toolset; the second covers the mutation,
/// journal, and undo surfaces unique to this server.
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

    /// <summary>The target file was binary and cannot be rewritten as text.</summary>
    public const string IsBinary = nameof(IsBinary);

    /// <summary>The target file exceeded the configured size cap.</summary>
    public const string TooLarge = nameof(TooLarge);

    /// <summary>The operation exceeded its wall-clock budget across the file set.</summary>
    public const string OperationBudgetExceeded = nameof(OperationBudgetExceeded);

    /// <summary>An argument was missing, malformed, or out of range.</summary>
    public const string InvalidArgument = nameof(InvalidArgument);

    /// <summary>An unexpected internal error; details go to the log, never the client.</summary>
    public const string InternalError = nameof(InternalError);

    /// <summary>The write target is ignored by a <c>.gitignore</c>/<c>.mcpignore</c> rule.</summary>
    public const string Ignored = nameof(Ignored);

    /// <summary>The file's encoding was detected below the rewrite-confidence threshold and no explicit source encoding was supplied.</summary>
    public const string LowConfidenceEncoding = nameof(LowConfidenceEncoding);

    /// <summary>The number of matches that would be rewritten did not equal the caller's expected count; the call aborted before any write.</summary>
    public const string ExpectedMatchCountMismatch = nameof(ExpectedMatchCountMismatch);

    /// <summary>The pattern matched nothing in any rewritable file.</summary>
    public const string NoMatches = nameof(NoMatches);

    /// <summary>An undo target's current content hash no longer equals the recorded post-image hash; it was left untouched.</summary>
    public const string UndoHashMismatch = nameof(UndoHashMismatch);

    /// <summary>An undo target's journal path no longer confines to the root; it was skipped, never written.</summary>
    public const string UndoOutOfRoot = nameof(UndoOutOfRoot);

    /// <summary>An undo target's journal path is now denylisted; it was skipped, never written.</summary>
    public const string UndoDenied = nameof(UndoDenied);

    /// <summary>No batch exists for the supplied identifier.</summary>
    public const string BatchNotFound = nameof(BatchNotFound);

    /// <summary>The atomic write or rename failed for a target the gate had already accepted.</summary>
    public const string WriteFailed = nameof(WriteFailed);
}