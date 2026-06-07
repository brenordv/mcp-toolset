using System.Globalization;
using System.Text.Json;
using Serilog.Events;
using Serilog.Formatting;

namespace RaccoonNinja.McpToolset.Server.GitOps.Logging;

/// <summary>
/// Serilog text formatter that produces single-line JSON containing only the
/// allowlisted property names (see <see cref="LogFields"/>). Unknown properties
/// are silently dropped, so a future bug cannot leak a redacted value through a
/// misspelled key.
/// </summary>
public sealed class AllowlistJsonFormatter : ITextFormatter
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        SkipValidation = false,
    };

    public void Format(LogEvent logEvent, TextWriter output)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString(LogFields.Ts, logEvent.Timestamp.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture));
            writer.WriteString(LogFields.Level, logEvent.Level.ToString().ToUpperInvariant());
            writer.WriteString(LogFields.Service, LogFields.ServiceName);
            writer.WriteString(LogFields.Message, logEvent.RenderMessage(CultureInfo.InvariantCulture));

            foreach (var (key, value) in logEvent.Properties)
            {
                if (!LogFields.Allowed.Contains(key)) continue;
                if (key == LogFields.Service || key == LogFields.Message || key == LogFields.Ts || key == LogFields.Level)
                    continue;
                writer.WritePropertyName(key);
                WriteValue(writer, value);
            }

            if (logEvent.Exception != null)
            {
                // Exception text is server-emitted (control-stripped to avoid leaking weird bytes into JSON).
                writer.WriteString(LogFields.StderrTail, LogScrubbing.ScrubStderrTail(System.Text.Encoding.UTF8.GetBytes(logEvent.Exception.ToString())));
            }

            writer.WriteEndObject();
        }
        buffer.Position = 0;
        var line = new StreamReader(buffer).ReadToEnd();
        output.WriteLine(line);
    }

    private static void WriteValue(Utf8JsonWriter writer, LogEventPropertyValue value)
    {
        switch (value)
        {
            case ScalarValue sv:
                WriteScalar(writer, sv.Value);
                break;

            case SequenceValue seq:
                writer.WriteStartArray();
                foreach (var item in seq.Elements) WriteValue(writer, item);
                writer.WriteEndArray();
                break;

            case StructureValue structure:
                writer.WriteStartObject();
                foreach (var prop in structure.Properties)
                {
                    writer.WritePropertyName(prop.Name);
                    WriteValue(writer, prop.Value);
                }
                writer.WriteEndObject();
                break;

            case DictionaryValue dict:
                writer.WriteStartObject();
                foreach (var kvp in dict.Elements)
                {
                    writer.WritePropertyName(kvp.Key.Value?.ToString() ?? string.Empty);
                    WriteValue(writer, kvp.Value);
                }
                writer.WriteEndObject();
                break;

            default:
                writer.WriteStringValue(value.ToString());
                break;
        }
    }

    private static void WriteScalar(Utf8JsonWriter writer, object scalar)
    {
        switch (scalar)
        {
            case null:
                writer.WriteNullValue();
                break;

            case bool b:
                writer.WriteBooleanValue(b);
                break;

            case int i:
                writer.WriteNumberValue(i);
                break;

            case long l:
                writer.WriteNumberValue(l);
                break;

            case double d:
                writer.WriteNumberValue(d);
                break;

            case float f:
                writer.WriteNumberValue(f);
                break;

            case decimal dec:
                writer.WriteNumberValue(dec);
                break;

            case DateTime dt:
                writer.WriteStringValue(dt.ToString("o"));
                break;

            case DateTimeOffset dto:
                writer.WriteStringValue(dto.ToString("o"));
                break;

            case IDictionary<string, object> dict:
                writer.WriteStartObject();
                foreach (var kvp in dict)
                {
                    writer.WritePropertyName(kvp.Key);
                    WriteScalar(writer, kvp.Value);
                }
                writer.WriteEndObject();
                break;

            case IEnumerable<object> seq:
                writer.WriteStartArray();
                foreach (var item in seq) WriteScalar(writer, item);
                writer.WriteEndArray();
                break;

            default:
                writer.WriteStringValue(scalar.ToString());
                break;
        }
    }
}