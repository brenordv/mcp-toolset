using RaccoonNinja.McpToolset.Server.GitOps.Logging;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Logging;

public class StdoutSentinelTests
{
    [Fact]
    public void Install_And_Uninstall_Round_Trip()
    {
        StdoutSentinel.Install();
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
    public void Install_Is_Idempotent()
    {
        StdoutSentinel.Install();
        StdoutSentinel.Install();
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