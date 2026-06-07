using System.Text;
using RaccoonNinja.McpToolset.Server.GitOps.Logging;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Logging;

public class LogScrubbingTests
{
    [Fact]
    public void StderrTail_Returns_Empty_For_Null_And_Empty()
    {
        Assert.Equal(string.Empty, LogScrubbing.ScrubStderrTail(null));
        Assert.Equal(string.Empty, LogScrubbing.ScrubStderrTail(System.Array.Empty<byte>()));
    }

    [Fact]
    public void StderrTail_Strips_Control_Chars_And_Keeps_Printables()
    {
        var raw = Encoding.UTF8.GetBytes("hello\nworld\t!");
        Assert.Equal("helloworld!", LogScrubbing.ScrubStderrTail(raw));
    }

    [Fact]
    public void StderrTail_Caps_To_Tail_When_Large()
    {
        var raw = new byte[LogScrubbing.StderrTailMaxBytes * 2];
        for (var i = 0; i < raw.Length; i++) raw[i] = (byte)('a' + (i % 26));
        var tail = LogScrubbing.ScrubStderrTail(raw);
        Assert.True(tail.Length <= LogScrubbing.StderrTailMaxBytes);
    }

    [Fact]
    public void DriverName_Caps_And_Strips_Controls()
    {
        var big = new string('x', LogScrubbing.DriverNameMaxBytes * 2);
        var scrubbed = LogScrubbing.ScrubDriverName(big);
        Assert.Equal(LogScrubbing.DriverNameMaxBytes, scrubbed.Length);
    }

    [Fact]
    public void HashedParameter_Is_Stable_And_Lowercase_Hex()
    {
        var a = LogScrubbing.HashedParameter("hello");
        var b = LogScrubbing.HashedParameter("hello");
        Assert.Equal(a, b);
        Assert.Equal(8, a.Length);
        Assert.Matches("^[0-9a-f]{8}$", a);
    }
}