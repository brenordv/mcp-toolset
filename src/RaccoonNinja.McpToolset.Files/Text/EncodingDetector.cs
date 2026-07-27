using System.Text;
using UtfUnknown;

namespace RaccoonNinja.McpToolset.Files.Text;

/// <summary>
/// Detects text encoding with a four-rung ladder whose order matters: byte-order mark, then a NUL
/// scan run <em>before</em> any UTF-8 attempt, then strict UTF-8, then a universal charset guess.
/// The NUL scan is the load-bearing step: a BOM-less UTF-16 file holding only ASCII is also valid
/// UTF-8 (every other byte is a legal single-byte sequence), so a naive UTF-8-first detector accepts
/// it and hands back a string full of embedded NULs, destroying the file on the next write.
/// </summary>
public sealed class EncodingDetector : IEncodingDetector
{
    private const int NulScanWindow = 8192;
    private const double DefiniteConfidence = 1d;
    private const double NulScanConfidence = 0.9d;

    // A NUL "class" (byte index modulo 4) is treated as empty when its count is at most this share of
    // the populated classes, and the populated side must cover at least this share of the scan window.
    // Clean ASCII text in UTF-16/UTF-32 fills its classes exactly; the ratios tolerate the occasional
    // higher codepoint while still rejecting a binary file whose NULs fall in no regular pattern.
    private const double DominanceRatio = 0.25d;
    private const double MinNulFraction = 0.25d;

    static EncodingDetector()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <inheritdoc />
    public DetectedEncoding Detect(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (content.Length == 0)
        {
            return new DetectedEncoding
            {
                Name = "utf-8",
                Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                Confidence = DefiniteConfidence,
                HasBom = false,
                IsBinary = false,
                Step = EncodingDetectionStep.Empty,
            };
        }

        return DetectByBom(content)
               ?? DetectByNulScan(content)
               ?? (IsStrictUtf8(content) ? Utf8NoBom() : GuessCodepage(content));
    }

    /// <summary>Match a leading byte-order mark, checking UTF-32 LE before UTF-16 LE (their prefixes collide).</summary>
    private static DetectedEncoding DetectByBom(byte[] content)
    {
        if (StartsWith(content, 0x00, 0x00, 0xFE, 0xFF))
        {
            return Bom("utf-32be", new UTF32Encoding(bigEndian: true, byteOrderMark: false));
        }

        if (StartsWith(content, 0xFF, 0xFE, 0x00, 0x00))
        {
            return Bom("utf-32le", new UTF32Encoding(bigEndian: false, byteOrderMark: false));
        }

        if (StartsWith(content, 0xFE, 0xFF))
        {
            return Bom("utf-16be", new UnicodeEncoding(bigEndian: true, byteOrderMark: false));
        }

        if (StartsWith(content, 0xFF, 0xFE))
        {
            return Bom("utf-16le", new UnicodeEncoding(bigEndian: false, byteOrderMark: false));
        }

        if (StartsWith(content, 0xEF, 0xBB, 0xBF))
        {
            return Bom("utf-8", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        return null;
    }

    /// <summary>
    /// Classify by the positional pattern of NUL bytes in the first <see cref="NulScanWindow"/> bytes.
    /// Returns <c>null</c> when no NULs are present (a clean single-byte encoding, deferred to the
    /// UTF-8 rung); a binary result when NULs appear in no UTF-16/UTF-32 pattern.
    /// </summary>
    private static DetectedEncoding DetectByNulScan(byte[] content)
    {
        var window = Math.Min(content.Length, NulScanWindow);
        var counts = new int[4];
        var totalNul = 0;
        for (var i = 0; i < window; i++)
        {
            if (content[i] == 0)
            {
                counts[i % 4]++;
                totalNul++;
            }
        }

        if (totalNul == 0)
        {
            return null;
        }

        if (content.Length % 4 == 0)
        {
            if (IsQuadPattern(counts, emptyClass: 0, window))
            {
                return NulScanResult("utf-32le", new UTF32Encoding(bigEndian: false, byteOrderMark: false));
            }

            if (IsQuadPattern(counts, emptyClass: 3, window))
            {
                return NulScanResult("utf-32be", new UTF32Encoding(bigEndian: true, byteOrderMark: false));
            }
        }

        if (content.Length % 2 == 0)
        {
            var evenNul = counts[0] + counts[2];
            var oddNul = counts[1] + counts[3];
            if (IsHalfPattern(dense: oddNul, sparse: evenNul, window))
            {
                return NulScanResult("utf-16le", new UnicodeEncoding(bigEndian: false, byteOrderMark: false));
            }

            if (IsHalfPattern(dense: evenNul, sparse: oddNul, window))
            {
                return NulScanResult("utf-16be", new UnicodeEncoding(bigEndian: true, byteOrderMark: false));
            }
        }

        return new DetectedEncoding
        {
            Name = "binary",
            Encoding = null,
            Confidence = NulScanConfidence,
            HasBom = false,
            IsBinary = true,
            Step = EncodingDetectionStep.NulScan,
        };
    }

    /// <summary>A UTF-32 signature: three populated NUL classes and one near-empty class (the text byte position).</summary>
    private static bool IsQuadPattern(int[] counts, int emptyClass, int window)
    {
        var filledMin = int.MaxValue;
        var filledSum = 0;
        for (var cls = 0; cls < counts.Length; cls++)
        {
            if (cls == emptyClass)
            {
                continue;
            }

            filledMin = Math.Min(filledMin, counts[cls]);
            filledSum += counts[cls];
        }

        return filledMin > 0
               && counts[emptyClass] <= filledMin * DominanceRatio
               && filledSum >= window * MinNulFraction;
    }

    /// <summary>A UTF-16 signature: NULs concentrated on one parity (the <paramref name="dense"/> side) and near-absent on the other.</summary>
    private static bool IsHalfPattern(int dense, int sparse, int window)
        => dense > 0
           && sparse <= dense * DominanceRatio
           && dense >= window * MinNulFraction;

    /// <summary>Validate the whole payload as strict UTF-8; multi-byte UTF-8 is self-validating, so legacy 8-bit content rarely passes by accident.</summary>
    private static bool IsStrictUtf8(byte[] content)
    {
        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        try
        {
            strict.GetCharCount(content);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>No BOM, no NULs, invalid UTF-8: some 8-bit codepage. Ask the charset detector, falling back to Latin-1 (decodes any byte) at zero confidence.</summary>
    private static DetectedEncoding GuessCodepage(byte[] content)
    {
        var detected = CharsetDetector.DetectFromBytes(content)?.Detected;
        if (detected?.Encoding is not null)
        {
            return new DetectedEncoding
            {
                Name = detected.EncodingName?.ToLowerInvariant() ?? detected.Encoding.WebName,
                Encoding = detected.Encoding,
                Confidence = detected.Confidence,
                HasBom = false,
                IsBinary = false,
                Step = EncodingDetectionStep.CharsetGuess,
            };
        }

        return new DetectedEncoding
        {
            Name = "iso-8859-1",
            Encoding = Encoding.Latin1,
            Confidence = 0d,
            HasBom = false,
            IsBinary = false,
            Step = EncodingDetectionStep.CharsetGuess,
        };
    }

    private static DetectedEncoding Utf8NoBom()
        => new()
        {
            Name = "utf-8",
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Confidence = DefiniteConfidence,
            HasBom = false,
            IsBinary = false,
            Step = EncodingDetectionStep.StrictUtf8,
        };

    private static DetectedEncoding Bom(string name, Encoding encoding)
        => new()
        {
            Name = name,
            Encoding = encoding,
            Confidence = DefiniteConfidence,
            HasBom = true,
            IsBinary = false,
            Step = EncodingDetectionStep.Bom,
        };

    private static DetectedEncoding NulScanResult(string name, Encoding encoding)
        => new()
        {
            Name = name,
            Encoding = encoding,
            Confidence = NulScanConfidence,
            HasBom = false,
            IsBinary = false,
            Step = EncodingDetectionStep.NulScan,
        };

    private static bool StartsWith(byte[] content, params byte[] prefix)
    {
        if (content.Length < prefix.Length)
        {
            return false;
        }

        for (var i = 0; i < prefix.Length; i++)
        {
            if (content[i] != prefix[i])
            {
                return false;
            }
        }

        return true;
    }
}