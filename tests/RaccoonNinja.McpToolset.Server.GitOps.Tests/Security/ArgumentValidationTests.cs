using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Security;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Security;

public class ArgumentValidationTests
{
    [Fact]
    public void Null_AndEmptyAreAcceptedAsNoops()
    {
        // Act & Assert
        ArgumentValidation.RejectIfUnsafeValue("x", null);
        ArgumentValidation.RejectIfUnsafeValue("x", string.Empty);
    }

    [Theory]
    [InlineData("alice")]
    [InlineData("HEAD")]
    [InlineData("v1.0")]
    [InlineData("with\tab")]
    public void Safe_ValuesAreAccepted(string value)
    {
        // Act & Assert
        ArgumentValidation.RejectIfUnsafeValue("x", value);
    }

    [Fact]
    public void Value_WithNulIsRejected()
    {
        // Act & Assert
        Assert.Throws<RejectedArgumentException>(() => ArgumentValidation.RejectIfUnsafeValue("x", "a\0b"));
    }

    [Fact]
    public void Value_WithOtherControlCharIsRejected()
    {
        // Act & Assert
        Assert.Throws<RejectedArgumentException>(() => ArgumentValidation.RejectIfUnsafeValue("x", "a\bb"));
    }

    [Fact]
    public void Value_StartingWithDashIsRejected()
    {
        // Act & Assert
        Assert.Throws<RejectedArgumentException>(() => ArgumentValidation.RejectIfUnsafeValue("x", "-evil"));
    }
}