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
    public void Format_SearchAndPaginationFields_PassThrough()
    {
        // Arrange
        var logEvent = Event(
            exception: null,
            new LogEventProperty(LogFields.QueryMode, new ScalarValue("any_term_fallback")),
            new LogEventProperty(LogFields.PageItems, new ScalarValue(12)),
            new LogEventProperty(LogFields.Truncated, new ScalarValue(true)),
            new LogEventProperty(LogFields.CursorHash, new ScalarValue("abcd1234")),
            new LogEventProperty(LogFields.NotesScanned, new ScalarValue(30)),
            new LogEventProperty(LogFields.NotesMatched, new ScalarValue(4)),
            new LogEventProperty(LogFields.BytesScanned, new ScalarValue(4_096L)),
            new LogEventProperty(LogFields.SkippedUnreadable, new ScalarValue(1)),
            new LogEventProperty(LogFields.Reason, new ScalarValue("cursor_stale")));

        // Act
        var line = FormatLine(logEvent);

        // Assert
        using var body = JsonDocument.Parse(line);
        var root = body.RootElement;
        Assert.Equal("any_term_fallback", root.GetProperty(LogFields.QueryMode).GetString());
        Assert.Equal(12, root.GetProperty(LogFields.PageItems).GetInt32());
        Assert.True(root.GetProperty(LogFields.Truncated).GetBoolean());
        Assert.Equal("abcd1234", root.GetProperty(LogFields.CursorHash).GetString());
        Assert.Equal(30, root.GetProperty(LogFields.NotesScanned).GetInt32());
        Assert.Equal(4, root.GetProperty(LogFields.NotesMatched).GetInt32());
        Assert.Equal(4_096L, root.GetProperty(LogFields.BytesScanned).GetInt64());
        Assert.Equal(1, root.GetProperty(LogFields.SkippedUnreadable).GetInt32());
        Assert.Equal("cursor_stale", root.GetProperty(LogFields.Reason).GetString());
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