using RaccoonNinja.McpToolset.Server.GitOps.Repo;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Repo;

public class RefVerifierTests
{
    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef01234567")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public void IsResolvedObjectName_Accepts_Full_Lowercase_Hex(string value)
    {
        Assert.True(RefVerifier.IsResolvedObjectName(value));
    }

    [Theory]
    [InlineData("0123456789ABCDEF0123456789abcdef01234567")]
    [InlineData("abc123")]
    [InlineData("0123456789abcdef0123456789abcdef0123456")]
    [InlineData("0123456789abcdef0123456789abcdef0123456g")]
    [InlineData("0123456789abcdef 123456789abcdef01234567")]
    [InlineData("0123456789abcdef\n123456789abcdef01234567")]
    [InlineData("HEAD..HEAD")]
    [InlineData("")]
    public void IsResolvedObjectName_Rejects_NonObjectNames(string value)
    {
        Assert.False(RefVerifier.IsResolvedObjectName(value));
    }
}