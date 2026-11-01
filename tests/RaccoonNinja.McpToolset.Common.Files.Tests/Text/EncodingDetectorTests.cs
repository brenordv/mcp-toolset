using System.Text;
using RaccoonNinja.McpToolset.Common.Files.Text;

namespace RaccoonNinja.McpToolset.Common.Files.Tests.Text;

public sealed class EncodingDetectorTests
{
    private const int Windows1252CodePage = 1252;
    private const int ShiftJisCodePage = 932;
    private const int Utf8PrefixCharCount = 300;
    private const int AsciiPaddingLength = 2000;
    private const int ShiftJisRepeatCount = 40;
    private const int AsciiSentenceRepeatCount = 180;
    private const byte StrayLatin1Byte = 0xE9;
    private const byte ShiftJisLeadByte = 0x81;
    private const byte NonBreakingSpaceByte = 0xA0;
    private const string JapaneseSentence = "日本語のテキストファイルです。これは文字コードの検出をテストするための文章です。";
    private const string AsciiSentence = "plain ascii text ";

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

    [Theory]
    [MemberData(nameof(GuessesThatCannotReproduceBytes))]
    public void Detect_GuessThatCannotReproduceBytes_FallsBackToLatin1AtZeroConfidence(byte[] content)
    {
        // Act
        var result = _detector.Detect(content);

        // Assert
        Assert.Equal(EncodingDetectionStep.CharsetGuess, result.Step);
        Assert.Equal("iso-8859-1", result.Name);
        Assert.Equal(0d, result.Confidence);
        Assert.Equal(content, result.Encoding.GetBytes(result.Encoding.GetString(content)));
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

    public static TheoryData<byte[]> GuessesThatCannotReproduceBytes()
        => new()
        {
            Utf8PrefixWithLateInvalidByte(),
            ShiftJisPrefixWithLateInvalidByte(),
            Iso2022JpPrefixWithLateHighByte(),
            AsciiWithLoneNonBreakingSpaceByte(),
        };

    private static ReadOnlySpan<byte> LineTail => "x\n"u8;

    private static byte[] Utf8PrefixWithLateInvalidByte()
        => [.. Encoding.UTF8.GetBytes(new string('é', Utf8PrefixCharCount) + new string('a', AsciiPaddingLength)), StrayLatin1Byte, .. LineTail];

    private static byte[] ShiftJisPrefixWithLateInvalidByte()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var shiftJis = Encoding.GetEncoding(ShiftJisCodePage);
        return [.. shiftJis.GetBytes(string.Concat(Enumerable.Repeat(JapaneseSentence, ShiftJisRepeatCount))), ShiftJisLeadByte, .. " x\n"u8];
    }

    private static byte[] Iso2022JpPrefixWithLateHighByte()
        => [.. "\e$B"u8, .. "F|K\\8l"u8, .. "\e(B"u8, .. Encoding.ASCII.GetBytes(new string('a', AsciiPaddingLength)), StrayLatin1Byte, .. LineTail];

    private static byte[] AsciiWithLoneNonBreakingSpaceByte()
        => [.. Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat(AsciiSentence, AsciiSentenceRepeatCount))), NonBreakingSpaceByte, .. LineTail];

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