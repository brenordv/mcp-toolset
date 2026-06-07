using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Security;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Security;

public class ArgumentValidationTests
{
    [Fact]
    public void Null_And_Empty_Are_Accepted_As_Noops()
    {
        ArgumentValidation.RejectIfUnsafeValue("x", null);
        ArgumentValidation.RejectIfUnsafeValue("x", string.Empty);
    }

    [Theory]
    [InlineData("alice")]
    [InlineData("HEAD")]
    [InlineData("v1.0")]
    [InlineData("with\tab")]
    public void Safe_Values_Are_Accepted(string value)
    {
        ArgumentValidation.RejectIfUnsafeValue("x", value);
    }

    [Fact]
    public void Value_With_Nul_Is_Rejected()
    {
        Assert.Throws<RejectedArgumentException>(() => ArgumentValidation.RejectIfUnsafeValue("x", "a\0b"));
    }

    [Fact]
    public void Value_With_Other_Control_Char_Is_Rejected()
    {
        Assert.Throws<RejectedArgumentException>(() => ArgumentValidation.RejectIfUnsafeValue("x", "a\bb"));
    }

    [Fact]
    public void Value_Starting_With_Dash_Is_Rejected()
    {
        Assert.Throws<RejectedArgumentException>(() => ArgumentValidation.RejectIfUnsafeValue("x", "-evil"));
    }
}