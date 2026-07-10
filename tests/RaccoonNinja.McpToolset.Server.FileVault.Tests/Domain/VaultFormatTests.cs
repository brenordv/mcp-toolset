using RaccoonNinja.McpToolset.Server.FileVault.Domain;
using RaccoonNinja.McpToolset.Server.FileVault.Errors;
using RaccoonNinja.McpToolset.Server.FileVault.Extensions;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tests.Domain;

/// <summary>Covers <see cref="VaultFormatExtensions"/>: alias parsing, snapshot extensions, and wire-string round-trips.</summary>
public class VaultFormatTests
{
    [Theory]
    [InlineData("text", VaultFormat.Text)]
    [InlineData("txt", VaultFormat.Text)]
    [InlineData("TXT", VaultFormat.Text)]
    [InlineData("plain", VaultFormat.Text)]
    [InlineData("Plain", VaultFormat.Text)]
    [InlineData("markdown", VaultFormat.Markdown)]
    [InlineData("Markdown", VaultFormat.Markdown)]
    [InlineData("md", VaultFormat.Markdown)]
    [InlineData("MD", VaultFormat.Markdown)]
    [InlineData("json", VaultFormat.Json)]
    [InlineData("JSON", VaultFormat.Json)]
    [InlineData("yaml", VaultFormat.Yaml)]
    [InlineData("YAML", VaultFormat.Yaml)]
    [InlineData("yml", VaultFormat.Yaml)]
    [InlineData("YmL", VaultFormat.Yaml)]
    public void ParseVaultFormat_KnownAlias_MapsCaseInsensitively(string value, VaultFormat expected)
    {
        // Act
        var format = VaultFormatExtensions.ParseVaultFormat(value);

        // Assert
        Assert.Equal(expected, format);
    }

    [Fact]
    public void ParseVaultFormat_Null_DefaultsToText()
    {
        // Act
        var format = VaultFormatExtensions.ParseVaultFormat(null);

        // Assert
        Assert.Equal(VaultFormat.Text, format);
    }

    [Theory]
    [InlineData("")]
    [InlineData("toml")]
    [InlineData("rst")]
    [InlineData("text/plain")]
    public void ParseVaultFormat_UnknownFormat_ThrowsInvalidFormat(string value)
    {
        // Act
        Action act = () => VaultFormatExtensions.ParseVaultFormat(value);

        // Assert
        var exception = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.InvalidFormat, exception.Code);
    }

    [Fact]
    public void ParseOptionalVaultFormat_Null_ReturnsNull()
    {
        // Act
        var format = VaultFormatExtensions.ParseOptionalVaultFormat(null);

        // Assert
        Assert.Null(format);
    }

    [Fact]
    public void ParseOptionalVaultFormat_KnownAlias_DelegatesToParse()
    {
        // Act
        var format = VaultFormatExtensions.ParseOptionalVaultFormat("md");

        // Assert
        Assert.Equal(VaultFormat.Markdown, format);
    }

    [Fact]
    public void ParseOptionalVaultFormat_UnknownFormat_ThrowsInvalidFormat()
    {
        // Act
        Action act = () => VaultFormatExtensions.ParseOptionalVaultFormat("toml");

        // Assert
        var exception = Assert.Throws<VaultException>(act);
        Assert.Equal(VaultErrorCode.InvalidFormat, exception.Code);
    }

    [Theory]
    [InlineData(VaultFormat.Text, "txt")]
    [InlineData(VaultFormat.Markdown, "md")]
    [InlineData(VaultFormat.Json, "json")]
    [InlineData(VaultFormat.Yaml, "yaml")]
    public void Extension_Format_MapsToSnapshotExtension(VaultFormat format, string expected)
    {
        // Act
        var extension = format.Extension();

        // Assert
        Assert.Equal(expected, extension);
    }

    [Theory]
    [InlineData(VaultFormat.Text, "text")]
    [InlineData(VaultFormat.Markdown, "markdown")]
    [InlineData(VaultFormat.Json, "json")]
    [InlineData(VaultFormat.Yaml, "yaml")]
    public void ToWireString_Format_MapsToCanonicalString(VaultFormat format, string expected)
    {
        // Act
        var wire = format.ToWireString();

        // Assert
        Assert.Equal(expected, wire);
    }

    [Fact]
    public void ToWireString_EveryFormat_RoundTripsThroughParse()
    {
        // Arrange
        var formats = Enum.GetValues<VaultFormat>();

        // Act
        var roundTripped = formats.Select(f => VaultFormatExtensions.ParseVaultFormat(f.ToWireString()));

        // Assert
        Assert.Equal(formats, roundTripped);
    }
}