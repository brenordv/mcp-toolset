namespace RaccoonNinja.McpToolset.Server.TextEdit.Content;

/// <summary>
/// Why a single journal row was skipped during undo rather than restored. The first two are the security
/// skips: a journal path is untrusted input, so a row that no longer confines or is now denylisted is never
/// written back. The last two are safety skips: the file changed since the batch, or could not be read.
/// </summary>
public static class UndoSkipReason
{
    /// <summary>The journal path no longer resolves inside the root.</summary>
    public const string OutOfRoot = "out_of_root";

    /// <summary>The journal path is now on the secret denylist.</summary>
    public const string Denied = "denied";

    /// <summary>The file's current content hash no longer equals the recorded post-image; it was changed since the batch.</summary>
    public const string HashMismatch = "hash_mismatch";

    /// <summary>The file could not be read to check the hash.</summary>
    public const string IoError = "io_error";
}