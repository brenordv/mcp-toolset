using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using RaccoonNinja.McpToolset.Server.FileVault.Errors;
using YamlDotNet.RepresentationModel;

namespace RaccoonNinja.McpToolset.Server.FileVault.Services;

/// <summary>
/// Dotted key-path editing for <c>vault_edit_key</c>. JSON editing preserves key order
/// (<see cref="JsonObject"/> keeps insertion order) so only the targeted value changes; YAML is
/// normalized by its emitter, an accepted asymmetry for machine-managed structured files.
/// </summary>
public static class KeyEditor
{
    // Rust-parity output shape: the relaxed encoder emits non-ASCII (and HTML-sensitive)
    // characters raw and NewLine pins LF, matching serde_json on every OS — the default encoder
    // would rewrite every non-ASCII byte to a \uXXXX escape, and the default newline would make
    // the same edit produce different snapshot bytes (and blake3 hashes) on Windows vs Linux.
    // The output is a local plain-text snapshot, never embedded in HTML.
    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Set the value at <paramref name="keyPath"/> in a JSON document.</summary>
    /// <param name="source">The JSON document text.</param>
    /// <param name="keyPath">The dotted path (objects by key, arrays by index; empty segments filtered).</param>
    /// <param name="value">The new value.</param>
    /// <returns>The re-serialized document (pretty, order-preserving, no trailing newline).</returns>
    /// <exception cref="VaultException">
    /// Thrown with <see cref="VaultErrorCode.KeyPathNotFound"/> when the path does not resolve, or
    /// <see cref="VaultErrorCode.InvalidFormat"/> when the document does not parse.
    /// </exception>
    public static string SetJsonKey(string source, string keyPath, JsonElement value)
    {
        JsonNode root;
        try
        {
            root = JsonNode.Parse(source);
        }
        catch (JsonException ex)
        {
            throw VaultException.InvalidFormat($"parsing JSON document: {ex.Message}");
        }

        var segments = SplitKeyPath(keyPath);
        var node = root;
        try
        {
            for (var i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                var isLast = i == segments.Count - 1;
                switch (node)
                {
                    case JsonObject obj when obj.ContainsKey(segment):
                        if (isLast)
                        {
                            obj[segment] = JsonNode.Parse(value.GetRawText());
                        }
                        else
                        {
                            node = obj[segment];
                        }

                        break;

                    case JsonArray array when TryParseIndex(segment, array.Count, out var index):
                        if (isLast)
                        {
                            array[index] = JsonNode.Parse(value.GetRawText());
                        }
                        else
                        {
                            node = array[index];
                        }

                        break;

                    default:
                        throw VaultException.KeyPathNotFound(keyPath);
                }
            }

            return root.ToJsonString(PrettyJson);
        }
        catch (ArgumentException ex)
        {
            // JsonObject materializes lazily; a document with duplicate keys surfaces here on
            // first property access. A domain outcome, not an internal error.
            throw VaultException.InvalidFormat($"JSON document has duplicate keys: {ex.Message}");
        }
    }

    /// <summary>Set the value at <paramref name="keyPath"/> in a YAML document (first document only).</summary>
    /// <param name="source">The YAML document text.</param>
    /// <param name="keyPath">The dotted path (mappings by key, sequences by index; empty segments filtered).</param>
    /// <param name="value">The new value (converted from JSON).</param>
    /// <returns>The re-emitted document, starting with <c>---</c> and ending with a newline.</returns>
    /// <exception cref="VaultException">
    /// Thrown with <see cref="VaultErrorCode.KeyPathNotFound"/> when the path does not resolve, or
    /// <see cref="VaultErrorCode.InvalidFormat"/> when the document does not parse or is empty.
    /// </exception>
    public static string SetYamlKey(string source, string keyPath, JsonElement value)
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(source));
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw VaultException.InvalidFormat($"parsing YAML document: {ex.Message}");
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is null)
        {
            throw VaultException.InvalidFormat("YAML document is empty");
        }

        var segments = SplitKeyPath(keyPath);
        var root = stream.Documents[0].RootNode;
        SetYamlValue(root, segments, 0, keyPath, JsonToYaml(value));

        var writer = new StringWriter();
        stream.Save(writer, assignAnchors: false);
        return NormalizeYamlOutput(writer.ToString());
    }

    private static void SetYamlValue(YamlNode node, IReadOnlyList<string> segments, int index, string keyPath, YamlNode value)
    {
        var segment = segments[index];
        var isLast = index == segments.Count - 1;
        switch (node)
        {
            case YamlMappingNode mapping:
                var key = new YamlScalarNode(segment);
                if (!mapping.Children.ContainsKey(key))
                {
                    throw VaultException.KeyPathNotFound(keyPath);
                }

                if (isLast)
                {
                    mapping.Children[key] = value;
                }
                else
                {
                    SetYamlValue(mapping.Children[key], segments, index + 1, keyPath, value);
                }

                break;

            case YamlSequenceNode sequence when TryParseIndex(segment, sequence.Children.Count, out var position):
                if (isLast)
                {
                    sequence.Children[position] = value;
                }
                else
                {
                    SetYamlValue(sequence.Children[position], segments, index + 1, keyPath, value);
                }

                break;

            default:
                throw VaultException.KeyPathNotFound(keyPath);
        }
    }

    /// <summary>Split on <c>.</c> with empty segments filtered (<c>a..b</c> equals <c>a.b</c>); all-empty rejects.</summary>
    private static List<string> SplitKeyPath(string keyPath)
    {
        var segments = (keyPath ?? string.Empty)
            .Split('.')
            .Where(segment => segment.Length > 0)
            .ToList();
        if (segments.Count == 0)
        {
            throw VaultException.KeyPathNotFound(keyPath ?? string.Empty);
        }

        return segments;
    }

    private static bool TryParseIndex(string segment, int count, out int index)
        => int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out index)
           && index >= 0
           && index < count;

    /// <summary>Convert a JSON value into the equivalent YAML node.</summary>
    private static YamlNode JsonToYaml(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.Null => new YamlScalarNode("~"),
            JsonValueKind.True => new YamlScalarNode("true"),
            JsonValueKind.False => new YamlScalarNode("false"),
            JsonValueKind.Number => new YamlScalarNode(value.GetRawText()),
            JsonValueKind.String => YamlString(value.GetString()),
            JsonValueKind.Array => new YamlSequenceNode(value.EnumerateArray().Select(JsonToYaml)),
            JsonValueKind.Object => YamlMapping(value),
            _ => new YamlScalarNode(value.GetRawText()),
        };

    private static YamlMappingNode YamlMapping(JsonElement value)
    {
        var mapping = new YamlMappingNode();
        foreach (var property in value.EnumerateObject())
        {
            mapping.Add(new YamlScalarNode(property.Name), JsonToYaml(property.Value));
        }

        return mapping;
    }

    /// <summary>
    /// Emit strings that would otherwise round-trip as a different scalar type (numbers, booleans,
    /// null spellings) as double-quoted, so a string stays a string after re-parse.
    /// </summary>
    private static YamlScalarNode YamlString(string value)
    {
        var node = new YamlScalarNode(value ?? string.Empty);
        var looksTyped = string.IsNullOrWhiteSpace(value)
            || bool.TryParse(value, out _)
            || double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out _)
            || value is "~" or "null" or "Null" or "NULL";
        if (looksTyped)
        {
            node.Style = YamlDotNet.Core.ScalarStyle.DoubleQuoted;
        }

        return node;
    }

    /// <summary>
    /// Shape the emitter output like yaml-rust2: a leading <c>---</c> document marker, no
    /// trailing <c>...</c> end marker, and exactly one trailing newline.
    /// </summary>
    private static string NormalizeYamlOutput(string emitted)
    {
        var text = emitted.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
        if (text.EndsWith("\n...", StringComparison.Ordinal))
        {
            text = text[..^4].TrimEnd('\n');
        }

        if (!text.StartsWith("---", StringComparison.Ordinal))
        {
            text = "---\n" + text;
        }

        return text + "\n";
    }
}