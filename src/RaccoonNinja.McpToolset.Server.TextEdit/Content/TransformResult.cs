namespace RaccoonNinja.McpToolset.Server.TextEdit.Content;

/// <summary>
/// The output of an <see cref="ITextTransform"/> over one file's decoded text: the new text, an optional
/// override of whether the re-encoded file keeps a byte-order mark, and a count of the matches or edits the
/// transform found. The gate decides "changed" by comparing the re-encoded bytes to the original, so a
/// transform that returns the same text (and no BOM change) is a no-op that writes nothing.
/// </summary>
public sealed record TransformResult
{
    /// <summary>The transformed text.</summary>
    public string NewText { get; init; }

    /// <summary>Whether to keep, drop, or add a BOM on re-encode; <c>null</c> keeps the original file's BOM state.</summary>
    public bool? BomOverride { get; init; }

    /// <summary>How many matches (for a replace) or edits (for a normalize) the transform found in this file.</summary>
    public int MatchCount { get; init; }
}