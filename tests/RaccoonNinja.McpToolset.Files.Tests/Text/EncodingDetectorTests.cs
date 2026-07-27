using System.Text;
using RaccoonNinja.McpToolset.Files.Text;

namespace RaccoonNinja.McpToolset.Files.Tests.Text;

public sealed class EncodingDetectorTests
{
    private const int Windows1252CodePage = 1252;

    private readonly EncodingDetector _detector = new();

    [Fact]
    public void Detect_Utf8WithoutBom_IdentifiesUtf8()
    {
        // Arrange
        var content = new UTF8Encoding(false).GetBytes("Hello, world");

        // Act
        var result = _detector.Detect(content);

        // Assert
        Assert.Equal("utf-8", result.Name);
        Assert.False(result.HasBom);
        Assert.False(result.IsBinary);
        Assert.Equal(EncodingDetectionStep.StrictUtf8, result.Step);
    }

    [Fact]
    public void Detect_Utf8WithBom_IdentifiesUtf8WithBom()
    {
        // Arrange
        var content = WithPrefix([0xEF, 0xBB, 0xBF], new UTF8Encoding(false).GetBytes("Hello"));

        // Act
        var result = _detector.Detect(content);

        // Assert
        Assert.Equal("utf-8", result.Name);
        Assert.True(result.HasBom);
        Assert.Equal(EncodingDetectionStep.Bom, result.Step);
    }

    [Fact]
    public void Detect_Utf8Multibyte_IdentifiesUtf8()
    {
        // Arrange
        var content = new UTF8Encoding(false).GetBytes("café crème brûlée");

        // Act
        var result = _detector.Detect(content);

        // Assert
        Assert.Equal("utf-8", result.Name);
        Assert.Equal(1d, result.Confidence);
        Assert.False(result.IsBinary);
    }

    [Fact]
    public void Detect_Utf16LeWithoutBom_IdentifiesUtf16LeNotUtf8()
    {
        // Arrange
        var content = new UnicodeEncoding(false, false).GetBytes("using System;");

        // Act
        var result = _detector.Detect(content);

        // Assert
        Assert.Equal("utf-16le", result.Name);
        Assert.NotEqual("utf-8", result.Name);
        Assert.False(result.IsBinary);
        Assert.False(result.HasBom);
        Assert.Equal(EncodingDetectionStep.NulScan, result.Step);
    }

    [Fact]
    public void Detect_Utf16BeWithoutBom_IdentifiesUtf16Be()
    {
        // Arrange
        var content = new UnicodeEncoding(true, false).GetBytes("using System;");

        // Act
        var result = _detector.Detect(content);

        // Assert
        Assert.Equal("utf-16be", result.Name);
        Assert.False(result.IsBinary);
        Assert.Equal(EncodingDetectionStep.NulScan, result.Step);
    }

    [Fact]
    public void Detect_Utf16LeWithBom_IdentifiesUtf16LeWithBom()
    {
        // Arrange
        var content = WithPrefix([0xFF, 0xFE], new UnicodeEncoding(false, false).GetBytes("Hello"));

        // Act
        var result = _detector.Detect(content);

        // Assert
        Assert.Equal("utf-16le", result.Name);
        Assert.True(result.HasBom);
        Assert.Equal(EncodingDetectionStep.Bom, result.Step);
    }

    [Fact]
    public void Detect_Utf32LeWithBom_IdentifiesUtf32LeNotUtf16()
    {
        // Arrange
        var content = WithPrefix([0xFF, 0xFE, 0x00, 0x00], new UTF32Encoding(false, false).GetBytes("Hello"));

        // Act
        var result = _detector.Detect(content);

        // Assert
        Assert.Equal("utf-32le", result.Name);
        Assert.NotEqual("utf-16le", result.Name);
        Assert.True(result.HasBom);
        Assert.Equal(EncodingDetectionStep.Bom, result.Step);
    }

    [Fact]
    public void Detect_Utf32LeWithoutBom_IdentifiesUtf32Le()
    {
        // Arrange
        var content = new UTF32Encoding(false, false).GetBytes("namespace X;");

        // Act
        var result = _detector.Detect(content);

        // Assert
        Assert.Equal("utf-32le", result.Name);
        Assert.False(result.IsBinary);
        Assert.Equal(EncodingDetectionStep.NulScan, result.Step);
    }

    [Fact]
    public void Detect_Windows1252_ReachesCharsetGuessAndIsReadable()
    {
        // Arrange
        var content = Windows1252Bytes("Le café coûte à Noël, dépôt arrêté près du hôtel français.");

        // Act
        var result = _detector.Detect(content);

        // Assert
        Assert.Equal(EncodingDetectionStep.CharsetGuess, result.Step);
        Assert.False(result.IsBinary);
        Assert.NotNull(result.Encoding);
    }

    [Fact]
    public void Detect_BinaryPayload_IdentifiesBinary()
    {
        // Arrange
        var content = BinaryPngHeader();

        // Act
        var result = _detector.Detect(content);

        // Assert
        Assert.True(result.IsBinary);
        Assert.Null(result.Encoding);
        Assert.Equal(EncodingDetectionStep.NulScan, result.Step);
    }

    [Fact]
    public void Detect_EmptyInput_IdentifiesEmptyUtf8()
    {
        // Arrange
        var content = Array.Empty<byte>();

        // Act
        var result = _detector.Detect(content);

        // Assert
        Assert.Equal("utf-8", result.Name);
        Assert.False(result.IsBinary);
        Assert.Equal(EncodingDetectionStep.Empty, result.Step);
    }

    [Fact]
    public void Detect_BomOnly_IdentifiesUtf8WithBom()
    {
        // Arrange
        var content = new byte[] { 0xEF, 0xBB, 0xBF };

        // Act
        var result = _detector.Detect(content);

        // Assert
        Assert.Equal("utf-8", result.Name);
        Assert.True(result.HasBom);
        Assert.Equal(EncodingDetectionStep.Bom, result.Step);
    }

    [Fact]
    public void Detect_NullContent_Throws()
    {
        // Arrange
        // Act
        // Assert
        Assert.Throws<ArgumentNullException>(() => _detector.Detect(null));
    }

    #region Test Helpers

    private static byte[] WithPrefix(byte[] prefix, byte[] body)
    {
        var combined = new byte[prefix.Length + body.Length];
        prefix.CopyTo(combined, 0);
        body.CopyTo(combined, prefix.Length);
        return combined;
    }

    private static byte[] Windows1252Bytes(string text)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(Windows1252CodePage).GetBytes(text);
    }

    private static byte[] BinaryPngHeader()
        =>
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x10,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x04, 0x67, 0x41, 0x4D,
        ];

    #endregion
}