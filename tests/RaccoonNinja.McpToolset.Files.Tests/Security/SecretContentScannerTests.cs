using System.Text;
using RaccoonNinja.McpToolset.Files.Security;
using RaccoonNinja.McpToolset.Files.Text;

namespace RaccoonNinja.McpToolset.Files.Tests.Security;

public sealed class SecretContentScannerTests
{
    private readonly SecretContentScanner _scanner = new(new EncodingDetector());

    [Theory]
    [InlineData("-----BEGIN OPENSSH PRIVATE KEY-----\nMIIabc")]
    [InlineData("-----BEGIN ENCRYPTED PRIVATE KEY-----")]
    [InlineData("aws = \"AKIAIOSFODNN7EXAMPLE\"")]
    [InlineData("db=postgres://admin:s3cr3tpw@db.example.com:5432/app")]
    [InlineData("token=github_pat_11ABCDE0aFGHIJKLMNOPQRs")]
    [InlineData("stripe=sk_live_0123456789abcdef")]
    [InlineData("slack=xoxb-1234567890abcdef")]
    public void Scan_KnownSecret_IsWithheld(string content)
    {
        // Arrange
        // Act
        var result = _scanner.Scan(Encoding.UTF8.GetBytes(content));

        // Assert
        Assert.True(result.IsSecret);
    }

    [Fact]
    public void Scan_LengthSensitiveTokens_AreWithheld()
    {
        // Arrange
        var google = "key=AIza" + new string('a', 35);
        var azure = "AccountKey=" + new string('A', 88);
        var sendgrid = "SG." + new string('a', 22) + "." + new string('b', 43);
        var gocspx = "GOCSPX-" + new string('a', 28);

        // Act
        // Assert
        Assert.True(_scanner.Scan(Encoding.UTF8.GetBytes(google)).IsSecret);
        Assert.True(_scanner.Scan(Encoding.UTF8.GetBytes(azure)).IsSecret);
        Assert.True(_scanner.Scan(Encoding.UTF8.GetBytes(sendgrid)).IsSecret);
        Assert.True(_scanner.Scan(Encoding.UTF8.GetBytes(gocspx)).IsSecret);
    }

    [Theory]
    [InlineData("const answer = 42; // ordinary source, no secret")]
    [InlineData("{ \"IsEncrypted\": false, \"Values\": { \"Feature\": \"on\" } }")]
    [InlineData("appsettings has a ConnectionString key but no value here")]
    public void Scan_CleanContent_IsNotWithheld(string content)
    {
        // Arrange
        // Act
        var result = _scanner.Scan(Encoding.UTF8.GetBytes(content));

        // Assert
        Assert.False(result.IsSecret);
    }

    [Fact]
    public void Scan_Utf16Secret_IsWithheld()
    {
        // Arrange
        var bytes = Encoding.Unicode.GetBytes("aws=\"AKIAIOSFODNN7EXAMPLE\"");

        // Act
        var result = _scanner.Scan(bytes);

        // Assert
        Assert.True(result.IsSecret);
    }

    [Fact]
    public void Scan_BinaryContent_IsNotScanned()
    {
        // Arrange
        byte[] binary = [0x00, 0x01, 0x02, 0x41, 0x4B];

        // Act
        var result = _scanner.Scan(binary);

        // Assert
        Assert.False(result.IsSecret);
    }

    [Fact]
    public void Scan_Jwt_WithheldOnlyInAggressiveLayer()
    {
        // Arrange
        var jwt = "auth=eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";
        var aggressive = new SecretContentScanner(new EncodingDetector(), aggressive: true);

        // Act
        // Assert
        Assert.False(_scanner.Scan(Encoding.UTF8.GetBytes(jwt)).IsSecret);
        Assert.True(aggressive.Scan(Encoding.UTF8.GetBytes(jwt)).IsSecret);
    }

    [Fact]
    public void DetectorIds_AggressiveLayer_AddsToDefaults()
    {
        // Arrange
        var aggressive = new SecretContentScanner(new EncodingDetector(), aggressive: true);

        // Act
        // Assert
        Assert.Contains("pem_private_key", _scanner.DetectorIds);
        Assert.DoesNotContain("jwt", _scanner.DetectorIds);
        Assert.Contains("jwt", aggressive.DetectorIds);
    }

    [Fact]
    public void Scan_NullContent_Throws()
    {
        // Arrange
        // Act
        // Assert
        Assert.Throws<ArgumentNullException>(() => _scanner.Scan(null));
    }
}