namespace RaccoonNinja.McpToolset.Files.Text;

/// <summary>
/// Detects the text encoding of a byte payload without decoding it destructively, following the
/// order that avoids the classic corruption traps (byte-order mark, then a NUL scan <em>before</em>
/// any UTF-8 attempt, then strict UTF-8, then a charset guess).
/// </summary>
public interface IEncodingDetector
{
    /// <summary>Classify <paramref name="content"/> and report the encoding, confidence, and BOM state.</summary>
    /// <param name="content">The raw bytes to classify.</param>
    /// <returns>The detection result; <see cref="DetectedEncoding.IsBinary"/> marks a payload that must not be read as text.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="content"/> is <c>null</c>.</exception>
    DetectedEncoding Detect(byte[] content);
}