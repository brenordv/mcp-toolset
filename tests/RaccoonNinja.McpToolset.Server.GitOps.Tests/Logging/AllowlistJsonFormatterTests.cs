using System.Text.Json;
using RaccoonNinja.McpToolset.Server.GitOps.Logging;
using Serilog.Events;
using Serilog.Parsing;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Logging;

public class AllowlistJsonFormatterTests
{
    private const string LeakProbeSegment = "leak-probe";

    [Fact]
    public void Formatter_EmitsAllowedFieldsAndDropsUnknown()
    {
        // Arrange
        var formatter = new AllowlistJsonFormatter();
        var template = new MessageTemplateParser().Parse("{Event}");
        var properties = new[]
        {
            new LogEventProperty(LogFields.Event, new ScalarValue("argv_built")),
            new LogEventProperty(LogFields.Tool, new ScalarValue("git_status")),
            new LogEventProperty(LogFields.CallId, new ScalarValue(7)),
            new LogEventProperty("secret_param", new ScalarValue("oops")),
        };
        var ev = new LogEvent(
            timestamp: new System.DateTimeOffset(2026, 1, 2, 3, 4, 5, System.TimeSpan.Zero),
            level: LogEventLevel.Information,
            exception: null,
            messageTemplate: template,
            properties: properties);
        using var writer = new StringWriter();

        // Act
        formatter.Format(ev, writer);

        // Assert
        var line = writer.ToString().Trim();
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        Assert.Equal("argv_built", root.GetProperty(LogFields.Event).GetString());
        Assert.Equal("git_status", root.GetProperty(LogFields.Tool).GetString());
        Assert.Equal(7, root.GetProperty(LogFields.CallId).GetInt32());
        Assert.Equal(LogFields.ServiceName, root.GetProperty(LogFields.Service).GetString());
        Assert.False(root.TryGetProperty("secret_param", out _));
    }

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
            document.RootElement.GetProperty(LogFields.StderrTail).GetString());
        Assert.DoesNotContain(LeakProbeSegment, line, StringComparison.Ordinal);
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