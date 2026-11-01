using System.Globalization;
using System.Text.Json;
using Serilog.Events;
using Serilog.Formatting;

namespace RaccoonNinja.McpToolset.Server.FileVault.Logging;

/// <summary>
/// Serilog text formatter that produces single-line JSON containing only the allowlisted property
/// names (see <see cref="LogFields"/>). Unknown properties are silently dropped, so a future bug
/// cannot leak a redacted value through a misspelled key.
/// </summary>
public sealed class AllowlistJsonFormatter : ITextFormatter
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        SkipValidation = false,
    };

    /// <inheritdoc />
    public void Format(LogEvent logEvent, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(output);
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
                if (!LogFields.Allowed.Contains(key))
                {
                    continue;
                }

                if (key is LogFields.Service or LogFields.Message or LogFields.Ts or LogFields.Level)
                {
                    continue;
                }

                writer.WritePropertyName(key);
                WriteValue(writer, value);
            }

            if (logEvent.Exception is not null)
            {
                // Only the type chain is written, for every exception. A vault or MCP exception's message is client-facing
                // and can embed note content (a conflict carries the diff), and any other exception's message or stack trace
                // can carry an absolute path.
                writer.WriteString(LogFields.StderrTail, LogScrubbing.ExceptionTypeChain(logEvent.Exception));
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
            case ScalarValue scalar:
                WriteScalar(writer, scalar.Value);
                break;

            case SequenceValue sequence:
                writer.WriteStartArray();
                foreach (var item in sequence.Elements)
                {
                    WriteValue(writer, item);
                }

                writer.WriteEndArray();
                break;

            case StructureValue structure:
                writer.WriteStartObject();
                foreach (var property in structure.Properties)
                {
                    writer.WritePropertyName(property.Name);
                    WriteValue(writer, property.Value);
                }

                writer.WriteEndObject();
                break;

            case DictionaryValue dictionary:
                writer.WriteStartObject();
                foreach (var kvp in dictionary.Elements)
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

            case IDictionary<string, object> dictionary:
                writer.WriteStartObject();
                foreach (var kvp in dictionary)
                {
                    writer.WritePropertyName(kvp.Key);
                    WriteScalar(writer, kvp.Value);
                }

                writer.WriteEndObject();
                break;

            case IEnumerable<object> sequence:
                writer.WriteStartArray();
                foreach (var item in sequence)
                {
                    WriteScalar(writer, item);
                }

                writer.WriteEndArray();
                break;

            default:
                writer.WriteStringValue(Convert.ToString(scalar, CultureInfo.InvariantCulture));
                break;
        }
    }
}