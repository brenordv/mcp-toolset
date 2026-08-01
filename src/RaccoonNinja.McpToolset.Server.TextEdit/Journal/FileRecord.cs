namespace RaccoonNinja.McpToolset.Server.TextEdit.Journal;

/// <summary>
/// One journaled file row: a root-relative path, the pre-image hash and blob reference, the post-image
/// hash (null until the write is finalized), the persisted encoding-detection triple, and the outcome.
/// Undo restores a row from its pre-image blob when the file's current hash still equals
/// <see cref="PostHash"/> (or when the file is gone), and never from an out-of-root or denylisted path.
/// </summary>
public sealed record FileRecord
{
    /// <summary>The root-relative POSIX path of the file (never an absolute path).</summary>
    public string Path { get; init; }

    /// <summary>The BLAKE3 hash of the pre-image (the content before the batch's write).</summary>
    public string PreHash { get; init; }

    /// <summary>The content-addressed reference of the pre-image blob in the journal blob store.</summary>
    public string BlobRef { get; init; }

    /// <summary>The BLAKE3 hash of the post-image, or <c>null</c> while the row is still pending.</summary>
    public string PostHash { get; init; }

    /// <summary>The detected encoding name the file was rewritten through.</summary>
    public string Encoding { get; init; }

    /// <summary>The detection confidence for <see cref="Encoding"/>.</summary>
    public double EncodingConfidence { get; init; }

    /// <summary>The encoding-detection ladder step that decided the encoding.</summary>
    public string LadderStep { get; init; }

    /// <summary>Whether the caller supplied an explicit source encoding rather than relying on detection.</summary>
    public bool SourceEncodingSupplied { get; init; }

    /// <summary>The row outcome (<see cref="JournalOutcome.Pending"/> or <see cref="JournalOutcome.Changed"/>).</summary>
    public string Outcome { get; init; }
}