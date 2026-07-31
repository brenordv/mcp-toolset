using System.Text;
using RaccoonNinja.McpToolset.Server.TextEdit.Content;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Tests.Content;

public sealed class TextCodecTests
{
    [Fact]
    public void DecodeThenEncode_Utf8NoBom_RoundTripsByteIdentical()
    {
        // Arrange
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var bytes = "hello"u8.ToArray();

        // Act
        var text = TextCodec.Decode(bytes, encoding, hasBom: false);
        var roundTripped = TextCodec.Encode(text, encoding, withBom: false, "utf-8");

        // Assert
        Assert.Equal("hello", text);
        Assert.Equal(bytes, roundTripped);
    }

    [Fact]
    public void Decode_Utf16LeNoBom_ReturnsText()
    {
        // Arrange
        var encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: false);

        // Act
        var text = TextCodec.Decode([0x68, 0x00, 0x69, 0x00], encoding, hasBom: false);

        // Assert
        Assert.Equal("hi", text);
    }

    [Fact]
    public void DecodeThenEncode_Utf16LeWithBom_StripsThenRestoresTheMark()
    {
        // Arrange
        var encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: false);
        byte[] bytes = [0xFF, 0xFE, 0x68, 0x00, 0x69, 0x00];

        // Act
        var text = TextCodec.Decode(bytes, encoding, hasBom: true);
        var roundTripped = TextCodec.Encode(text, encoding, withBom: true, "utf-16le");

        // Assert
        Assert.Equal("hi", text);
        Assert.Equal(bytes, roundTripped);
    }

    [Fact]
    public void Encode_WithoutBom_DoesNotPrependTheMark()
    {
        // Arrange
        var encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: false);

        // Act
        var bytes = TextCodec.Encode("hi", encoding, withBom: false, "utf-16le");

        // Assert
        Assert.Equal([0x68, 0x00, 0x69, 0x00], bytes);
    }
}