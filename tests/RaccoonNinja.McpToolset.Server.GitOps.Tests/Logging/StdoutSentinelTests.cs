using RaccoonNinja.McpToolset.Server.GitOps.Logging;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Logging;

public class StdoutSentinelTests
{
    [Fact]
    public void Install_AndUninstallRoundTrip()
    {
        // Arrange
        StdoutSentinel.Install();

        // Act & Assert
        try
        {
            Assert.Throws<InvalidOperationException>(() => Console.Out.Write("nope"));
            Assert.Throws<InvalidOperationException>(() => Console.Out.WriteLine("nope"));
        }
        finally
        {
            StdoutSentinel.Uninstall();
        }
    }

    [Fact]
    public void Install_IsIdempotent()
    {
        // Arrange
        StdoutSentinel.Install();
        StdoutSentinel.Install();

        // Act & Assert
        try
        {
            Assert.Throws<InvalidOperationException>(() => Console.Out.Write("nope"));
        }
        finally
        {
            StdoutSentinel.Uninstall();
        }
    }
}