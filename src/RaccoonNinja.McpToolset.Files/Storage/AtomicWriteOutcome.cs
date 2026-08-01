namespace RaccoonNinja.McpToolset.Files.Storage;

/// <summary>The result of an <see cref="AtomicWriter.WriteNew"/> call.</summary>
public enum AtomicWriteOutcome
{
    /// <summary>The file was created and now holds the supplied content.</summary>
    Written,

    /// <summary>
    /// The destination already existed and was left untouched. Content-addressed callers treat this
    /// as success: because the path is derived from the content hash, an existing target already holds
    /// identical bytes.
    /// </summary>
    Skipped,
}