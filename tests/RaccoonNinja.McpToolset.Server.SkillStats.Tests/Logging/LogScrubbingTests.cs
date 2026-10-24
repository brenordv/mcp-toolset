using RaccoonNinja.McpToolset.Server.SkillStats.Logging;

namespace RaccoonNinja.McpToolset.Server.SkillStats.Tests.Logging;

public sealed class LogScrubbingTests
{
    private const string ProbeMessage = "x";

    [Fact]
    public void ExceptionTypeChain_ChainDeeperThanCap_StopsWithEllipsis()
    {
        // Arrange
        var exception = NestedChain(LogScrubbing.ExceptionChainMaxDepth + 2);

        // Act
        var chain = LogScrubbing.ExceptionTypeChain(exception);

        // Assert
        var expected = string.Join(
            " -> ",
            Enumerable.Repeat("System.InvalidOperationException", LogScrubbing.ExceptionChainMaxDepth)) + " -> ...";
        Assert.Equal(expected, chain);
    }

    #region Test Helpers

    private static InvalidOperationException NestedChain(int depth)
    {
        return depth == 1
            ? new InvalidOperationException(ProbeMessage)
            : new InvalidOperationException(ProbeMessage, NestedChain(depth - 1));
    }

    #endregion
}