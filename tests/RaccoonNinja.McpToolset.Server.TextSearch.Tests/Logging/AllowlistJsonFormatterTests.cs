using System.Text.Json;
using RaccoonNinja.McpToolset.Server.TextSearch.Logging;
using Serilog.Events;
using Serilog.Parsing;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tests.Logging;

public sealed class AllowlistJsonFormatterTests
{
    private const string LeakProbeSegment = "leak-probe";

    [Fact]
    public void Format_ExceptionWithPathInMessage_WritesTypeChainWithoutPath()
    {
        // Arrange
        var leakedPath = Path.Combine(Path.GetTempPath(), LeakProbeSegment, "secret.txt");
        var exception = new DirectoryNotFoundException(
            $"Could not find '{leakedPath}'",
            new IOException("inner", new InvalidOperationException("root cause")));

        // Act
        var line = Format(Event(exception));

        // Assert
        using var document = JsonDocument.Parse(line);
        Assert.Equal(
            "System.IO.DirectoryNotFoundException -> System.IO.IOException -> System.InvalidOperationException",
            document.RootElement.GetProperty(LogFields.ExceptionTail).GetString());
        Assert.DoesNotContain(LeakProbeSegment, line, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_NoException_OmitsExceptionTail()
    {
        // Arrange
        var logEvent = Event(null);

        // Act
        var line = Format(logEvent);

        // Assert
        using var document = JsonDocument.Parse(line);
        Assert.False(document.RootElement.TryGetProperty(LogFields.ExceptionTail, out _));
    }

    #region Test Helpers

    private static LogEvent Event(Exception exception)
    {
        return new LogEvent(
            timestamp: DateTimeOffset.UtcNow,
            level: LogEventLevel.Error,
            exception: exception,
            messageTemplate: new MessageTemplateParser().Parse("{Event}"),
            properties: [new LogEventProperty(LogFields.Event, new ScalarValue("probe"))]);
    }

    private static string Format(LogEvent logEvent)
    {
        using var writer = new StringWriter();
        new AllowlistJsonFormatter().Format(logEvent, writer);
        return writer.ToString().Trim();
    }

    #endregion
}