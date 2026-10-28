using System.Text;
using RaccoonNinja.McpToolset.Server.GitOps.Logging;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Logging;

public class LogScrubbingTests
{
    private const string ProbeMessage = "x";

    [Fact]
    public void StderrTail_ReturnsEmptyForNullAndEmpty()
    {
        // Act & Assert
        Assert.Equal(string.Empty, LogScrubbing.ScrubStderrTail(null));
        Assert.Equal(string.Empty, LogScrubbing.ScrubStderrTail(System.Array.Empty<byte>()));
    }

    [Fact]
    public void StderrTail_StripsControlCharsAndKeepsPrintables()
    {
        // Arrange
        var raw = Encoding.UTF8.GetBytes("hello\nworld\t!");

        // Act & Assert
        Assert.Equal("helloworld!", LogScrubbing.ScrubStderrTail(raw));
    }

    [Fact]
    public void StderrTail_CapsToTailWhenLarge()
    {
        // Arrange
        var raw = new byte[LogScrubbing.StderrTailMaxBytes * 2];
        for (var i = 0; i < raw.Length; i++) raw[i] = (byte)('a' + (i % 26));

        // Act
        var tail = LogScrubbing.ScrubStderrTail(raw);

        // Assert
        Assert.True(tail.Length <= LogScrubbing.StderrTailMaxBytes);
    }

    [Fact]
    public void HashedParameter_IsStableAndLowercaseHex()
    {
        // Act
        var a = LogScrubbing.HashedParameter("hello");
        var b = LogScrubbing.HashedParameter("hello");

        // Assert
        Assert.Equal(a, b);
        Assert.Equal(8, a.Length);
        Assert.Matches("^[0-9a-f]{8}$", a);
    }

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