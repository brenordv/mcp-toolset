using System.Text;

namespace RaccoonNinja.McpToolset.Files.Text;

/// <summary>
/// The outcome of encoding detection for a single byte payload. Reading is always allowed, so a
/// result is produced for every non-binary input; callers that intend to <em>rewrite</em> the file
/// gate on <see cref="Confidence"/> and refuse below their threshold.
/// </summary>
public sealed record DetectedEncoding
{
    /// <summary>Canonical lowercase encoding name (for example <c>utf-8</c>, <c>utf-16le</c>, <c>windows-1252</c>).</summary>
    public string Name { get; init; }

    /// <summary>
    /// The <see cref="System.Text.Encoding"/> to decode with, or <c>null</c> when <see cref="IsBinary"/>
    /// is <c>true</c>. Any byte-order mark is reported separately via <see cref="HasBom"/> and is not
    /// stripped by this type.
    /// </summary>
    public Encoding Encoding { get; init; }

    /// <summary>Detector confidence in the range 0 to 1; 1 for a definitive (BOM or valid UTF-8) result.</summary>
    public double Confidence { get; init; }

    /// <summary>Whether the payload began with a byte-order mark.</summary>
    public bool HasBom { get; init; }

    /// <summary>Whether the payload was classified as binary and must not be read as text or rewritten.</summary>
    public bool IsBinary { get; init; }

    /// <summary>Which rung of the detection ladder produced this result.</summary>
    public EncodingDetectionStep Step { get; init; }
}