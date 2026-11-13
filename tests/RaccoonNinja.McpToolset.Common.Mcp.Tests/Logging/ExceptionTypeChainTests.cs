using RaccoonNinja.McpToolset.Common.Mcp.Logging;

namespace RaccoonNinja.McpToolset.Common.Mcp.Tests.Logging;

public sealed class ExceptionTypeChainTests
{
    private const string ProbeMessage = "x";

    [Fact]
    public void Render_ChainDeeperThanCap_StopsWithEllipsis()
    {
        // Arrange
        var exception = NestedChain(ExceptionTypeChain.MaxDepth + 2);

        // Act
        var chain = ExceptionTypeChain.Render(exception);

        // Assert
        var expected = string.Join(
            " -> ",
            Enumerable.Repeat("System.InvalidOperationException", ExceptionTypeChain.MaxDepth)) + " -> ...";
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