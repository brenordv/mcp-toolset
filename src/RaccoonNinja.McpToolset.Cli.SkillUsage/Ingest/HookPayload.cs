using System.Text.Json;

namespace RaccoonNinja.McpToolset.Cli.SkillUsage.Ingest;

/// <summary>
/// The fields extracted from a PostToolUse hook payload. Extraction walks the parsed JSON with
/// <c>TryGetProperty</c> rather than deserializing into a type, so an unknown or missing field can
/// never throw. The skill name comes from <c>tool_input.skill</c> then
/// <c>tool_input.name</c>; the args from <c>tool_input.args</c> then <c>tool_input.input</c>.
/// </summary>
internal sealed record HookPayload
{
    /// <summary>The raw, unnormalized skill name, or <c>null</c> when none was present.</summary>
    public string RawSkill { get; private init; }

    /// <summary>The skill input as text (object/array values re-serialized as compact JSON), or <c>null</c>.</summary>
    public string Args { get; private init; }

    /// <summary>The session id from the top-level payload, or <c>null</c>.</summary>
    public string SessionId { get; private init; }

    /// <summary>The workspace directory from the top-level payload, or <c>null</c>.</summary>
    public string Cwd { get; private init; }

    /// <summary>The top-level property names of <c>tool_input</c>, for shape-drift diagnosis in skip lines.</summary>
    public IReadOnlyList<string> ToolInputKeys { get; private init; } = [];

    /// <summary>Extract the recorded fields from a parsed hook payload.</summary>
    /// <param name="root">The root element of the parsed payload.</param>
    /// <returns>The extracted fields.</returns>
    public static HookPayload Extract(JsonElement root)
    {
        var sessionId = ReadString(root, "session_id");
        var cwd = ReadString(root, "cwd");

        string rawSkill = null;
        string args = null;
        var keys = new List<string>();

        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("tool_input", out var toolInput)
            && toolInput.ValueKind == JsonValueKind.Object)
        {
            rawSkill = ReadString(toolInput, "skill") ?? ReadString(toolInput, "name");
            args = ReadArgs(toolInput, "args") ?? ReadArgs(toolInput, "input");
            keys.AddRange(toolInput.EnumerateObject().Select(property => property.Name));
        }

        return new HookPayload
        {
            RawSkill = rawSkill,
            Args = args,
            SessionId = sessionId,
            Cwd = cwd,
            ToolInputKeys = keys,
        };
    }

    private static string ReadString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

    private static string ReadArgs(JsonElement toolInput, string name)
    {
        if (!toolInput.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Object or JsonValueKind.Array => JsonSerializer.Serialize(value),
            _ => value.GetRawText(),
        };
    }
}