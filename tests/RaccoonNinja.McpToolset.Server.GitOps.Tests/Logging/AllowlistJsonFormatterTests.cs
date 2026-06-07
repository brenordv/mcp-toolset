using System.Text.Json;
using RaccoonNinja.McpToolset.Server.GitOps.Logging;
using Serilog.Events;
using Serilog.Parsing;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Logging;

public class AllowlistJsonFormatterTests
{
    [Fact]
    public void Formatter_Emits_Allowed_Fields_And_Drops_Unknown()
    {
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
        formatter.Format(ev, writer);

        var line = writer.ToString().Trim();
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        Assert.Equal("argv_built", root.GetProperty(LogFields.Event).GetString());
        Assert.Equal("git_status", root.GetProperty(LogFields.Tool).GetString());
        Assert.Equal(7, root.GetProperty(LogFields.CallId).GetInt32());
        Assert.Equal(LogFields.ServiceName, root.GetProperty(LogFields.Service).GetString());
        Assert.False(root.TryGetProperty("secret_param", out _));
    }
}