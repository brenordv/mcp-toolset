using System.Text;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Security;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Security;

public class FilterDriverPinsTests
{
    [Theory]
    [InlineData("status", true)]
    [InlineData("diff", true)]
    [InlineData("blame", true)]
    [InlineData("log", false)]
    [InlineData("show", false)]
    [InlineData("grep", false)]
    [InlineData("ls-files", false)]
    [InlineData("config", false)]
    public void AppliesTo_Subcommand_ReturnsWhetherGitCanRunACleanFilter(string subcommand, bool expected)
    {
        // Act
        var applies = FilterDriverPins.AppliesTo(subcommand);

        // Assert
        Assert.Equal(expected, applies);
    }

    [Fact]
    public void ParseDriverNames_SeveralKeysPerDriver_ReturnsEachDriverOnceInListingOrder()
    {
        // Arrange
        var listing = Encoding.UTF8.GetBytes("filter.lfs.clean\0filter.lfs.smudge\0filter.crypt.clean\0filter.lfs.required\0");

        // Act
        var names = FilterDriverPins.ParseDriverNames(listing);

        // Assert
        Assert.Equal(["lfs", "crypt"], names);
    }

    [Theory]
    [InlineData("filter.a.b c.process\0", "a.b c")]
    [InlineData("filter.LFS.clean\0", "LFS")]
    public void ParseDriverNames_OneKey_ReturnsTheSubsectionAsWritten(string listing, string expected)
    {
        // Act
        var names = FilterDriverPins.ParseDriverNames(Encoding.UTF8.GetBytes(listing));

        // Assert
        Assert.Equal([expected], names);
    }

    [Theory]
    [InlineData("")]
    [InlineData("filter.orphan\0")]
    public void ParseDriverNames_NoDriverPart_ReturnsNoDriver(string listing)
    {
        // Act
        var names = FilterDriverPins.ParseDriverNames(Encoding.UTF8.GetBytes(listing));

        // Assert
        Assert.Empty(names);
    }

    [Fact]
    public void ParseDriverNames_NameWithEquals_ThrowsGitCommandException()
    {
        // Arrange
        var listing = Encoding.UTF8.GetBytes("filter.a=b.clean\0");

        // Act
        var act = () => FilterDriverPins.ParseDriverNames(listing);

        // Assert
        Assert.Throws<GitCommandException>(act);
    }

    [Fact]
    public void ParseDriverNames_InvalidUtf8_ThrowsGitCommandException()
    {
        // Arrange
        byte[] listing = [.. Encoding.ASCII.GetBytes("filter."), 0xFF, .. Encoding.ASCII.GetBytes(".clean\0")];

        // Act
        var act = () => FilterDriverPins.ParseDriverNames(listing);

        // Assert
        Assert.Throws<GitCommandException>(act);
    }
}