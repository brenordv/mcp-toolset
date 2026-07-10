using System.Text.Json;
using ModelContextProtocol;
using RaccoonNinja.McpToolset.Server.FileVault.Errors;
using RaccoonNinja.McpToolset.Server.FileVault.Logging;
using Serilog.Events;
using Serilog.Parsing;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tests.Logging;

/// <summary>Tests for <see cref="AllowlistJsonFormatter"/>: field allowlisting and the client-visible-exception redaction rule.</summary>
public class AllowlistJsonFormatterTests
{
    [Fact]
    public void Format_AllowlistedProperties_PassThroughAndUnknownAreDropped()
    {
        // Arrange
        var logEvent = Event(
            exception: null,
            new LogEventProperty(LogFields.Tool, new ScalarValue("vault_save")),
            new LogEventProperty(LogFields.DurationMs, new ScalarValue(42L)),
            new LogEventProperty("summary", new ScalarValue("free text that must never leak")),
            new LogEventProperty("Outcome", new ScalarValue("Succeeded")));

        // Act
        var line = FormatLine(logEvent);

        // Assert
        using var body = JsonDocument.Parse(line);
        var root = body.RootElement;
        Assert.Equal("vault_save", root.GetProperty(LogFields.Tool).GetString());
        Assert.Equal(42L, root.GetProperty(LogFields.DurationMs).GetInt64());
        Assert.False(root.TryGetProperty("summary", out _));
        Assert.False(root.TryGetProperty("Outcome", out _));
        Assert.DoesNotContain("free text that must never leak", line);
    }

    [Fact]
    public void Format_SplitHintFields_PassThrough()
    {
        // Arrange
        var logEvent = Event(
            exception: null,
            new LogEventProperty(LogFields.CommittedChars, new ScalarValue(14_001)),
            new LogEventProperty(LogFields.SplitHintChars, new ScalarValue(14_000)));

        // Act
        var line = FormatLine(logEvent);

        // Assert
        using var body = JsonDocument.Parse(line);
        Assert.Equal(14_001, body.RootElement.GetProperty(LogFields.CommittedChars).GetInt32());
        Assert.Equal(14_000, body.RootElement.GetProperty(LogFields.SplitHintChars).GetInt32());
    }

    [Fact]
    public void Format_VaultException_RecordsOnlyTheTypeName()
    {
        // Arrange
        var exception = VaultException.ConflictWithHint(currentVersion: 2, baseVersion: 1, diff: "-secret line");

        // Act
        var line = FormatLine(Event(exception));

        // Assert
        using var body = JsonDocument.Parse(line);
        Assert.Equal(nameof(VaultException), body.RootElement.GetProperty(LogFields.StderrTail).GetString());
        Assert.DoesNotContain("version conflict", line);
        Assert.DoesNotContain("-secret line", line);
    }

    [Fact]
    public void Format_McpException_RecordsOnlyTheTypeName()
    {
        // Arrange
        var exception = new McpException("{\"error\":{\"code\":\"conflict\",\"diff\":\"-secret\"}}");

        // Act
        var line = FormatLine(Event(exception));

        // Assert
        using var body = JsonDocument.Parse(line);
        Assert.Equal(nameof(McpException), body.RootElement.GetProperty(LogFields.StderrTail).GetString());
        Assert.DoesNotContain("-secret", line);
        Assert.DoesNotContain("conflict", line);
    }

    [Fact]
    public void Format_InternalException_KeepsScrubbedFullText()
    {
        // Arrange
        var exception = new InvalidOperationException("boom at C:\\path");

        // Act
        var line = FormatLine(Event(exception));

        // Assert
        using var body = JsonDocument.Parse(line);
        Assert.Contains("boom", body.RootElement.GetProperty(LogFields.StderrTail).GetString());
    }

    [Fact]
    public void Format_AnyEvent_EmitsOneLineOfValidJsonWithEnvelopeFields()
    {
        // Arrange
        var logEvent = Event(exception: null);

        // Act
        var line = FormatLine(logEvent);

        // Assert
        Assert.DoesNotContain("\n", line);
        using var body = JsonDocument.Parse(line);
        var root = body.RootElement;
        Assert.Equal("2026-01-02T03:04:05.0000000Z", root.GetProperty(LogFields.Ts).GetString());
        Assert.Equal("INFORMATION", root.GetProperty(LogFields.Level).GetString());
        Assert.Equal(LogFields.ServiceName, root.GetProperty(LogFields.Service).GetString());
        Assert.Equal("hello vault log", root.GetProperty(LogFields.Message).GetString());
    }

    /// <summary>Build a minimal Information-level event with a plain-text template.</summary>
    private static LogEvent Event(Exception exception, params LogEventProperty[] properties)
        => new(
            timestamp: new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            level: LogEventLevel.Information,
            exception: exception,
            messageTemplate: new MessageTemplateParser().Parse("hello vault log"),
            properties: properties);

    /// <summary>Run the formatter and return the single emitted line.</summary>
    private static string FormatLine(LogEvent logEvent)
    {
        var formatter = new AllowlistJsonFormatter();
        using var writer = new StringWriter();
        formatter.Format(logEvent, writer);
        return writer.ToString().Trim();
    }
}