namespace RaccoonNinja.McpToolset.Files.Text;

/// <summary>
/// Identifies which rung of the <see cref="EncodingDetector"/> ladder decided an encoding.
/// Numbering starts at 1 so <c>default</c> (0) always signals an unassigned value.
/// </summary>
public enum EncodingDetectionStep
{
    /// <summary>Decided by a byte-order mark or magic-byte prefix (definitive).</summary>
    Bom = 1,

    /// <summary>Decided by scanning for embedded NUL bytes and their positional pattern.</summary>
    NulScan = 2,

    /// <summary>Decided by a successful strict UTF-8 validation pass.</summary>
    StrictUtf8 = 3,

    /// <summary>Decided by the universal charset detector's best guess for an 8-bit codepage.</summary>
    CharsetGuess = 4,

    /// <summary>The input was empty; treated as UTF-8 by convention.</summary>
    Empty = 5,
}