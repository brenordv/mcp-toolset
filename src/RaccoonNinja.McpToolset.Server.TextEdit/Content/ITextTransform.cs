namespace RaccoonNinja.McpToolset.Server.TextEdit.Content;

/// <summary>
/// A pure text-to-text transform applied to one file's decoded content by the write gate. Implementations
/// operate on the raw decoded string (never a terminator-free line model), so mixed line endings are
/// preserved unless the transform is explicitly asked to change them.
/// </summary>
public interface ITextTransform
{
    /// <summary>Transform <paramref name="text"/> and report the new text plus how many matches or edits were found.</summary>
    /// <param name="text">The file's decoded text, with any leading byte-order mark already removed.</param>
    /// <returns>The transform result.</returns>
    TransformResult Transform(string text);
}