namespace RaccoonNinja.McpToolset.Server.TextEdit.Content;

/// <summary>
/// The per-file reasons the write gate declines to rewrite a file. Each is a stable wire string reported
/// in the tool result's <c>refusal_reason</c> and counted in the session metrics; a spike in any of them
/// is the signal that something is probing the boundary.
/// </summary>
public static class RefusalReason
{
    /// <summary>The path resolved outside the confinement root.</summary>
    public const string OutOfRoot = "out_of_root";

    /// <summary>The path is on the non-overridable secret denylist.</summary>
    public const string Denied = "denied";

    /// <summary>The path is ignored by a project ignore rule (<c>.gitignore</c>, an agent-ignore file, or <c>.mcpignore</c>), ancestor or leaf.</summary>
    public const string Ignored = "ignored";

    /// <summary>The path does not exist.</summary>
    public const string NotFound = "not_found";

    /// <summary>The path resolved to a directory, where a file was required.</summary>
    public const string IsDirectory = "is_directory";

    /// <summary>The file is binary and cannot be rewritten as text.</summary>
    public const string Binary = "binary";

    /// <summary>The file exceeds the configured size cap.</summary>
    public const string TooLarge = "too_large";

    /// <summary>The detected encoding was below the rewrite-confidence threshold and no source encoding was supplied.</summary>
    public const string LowConfidenceEncoding = "low_confidence_encoding";

    /// <summary>The replacement regex hit its per-match timeout on this file (a ReDoS guard).</summary>
    public const string RegexTimeout = "regex_timeout";

    /// <summary>The atomic write or rename failed for a file the gate had already accepted; its journal row stays restorable.</summary>
    public const string WriteFailed = "write_failed";

    /// <summary>A filesystem error prevented reading the file.</summary>
    public const string IoError = "io_error";
}