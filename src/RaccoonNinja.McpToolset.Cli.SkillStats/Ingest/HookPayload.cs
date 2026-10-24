using System.Text.Json;

namespace RaccoonNinja.McpToolset.Cli.SkillStats.Ingest;

/// <summary>
/// The fields extracted from a Claude Code hook payload. Two shapes are read: the
/// <c>UserPromptExpansion</c> shape, where the command name and the typed arguments are the root fields
/// <c>command_name</c> and <c>command_args</c>, and the <c>PostToolUse</c> tool-call shape, where they sit
/// under <c>tool_input</c> as <c>skill</c> or <c>name</c> and <c>args</c> or <c>input</c>. The tool-call
/// shape is read first, so a root <c>command_name</c> never shadows a tool payload. The root
/// <c>prompt</c> field, the whole typed line, is never read. Extraction walks the parsed JSON with
/// <c>TryGetProperty</c> rather than deserializing into a type, so an unknown or missing field can
/// never throw.
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

    /// <summary>The property names of the root object, for shape-drift diagnosis in skip lines.</summary>
    public IReadOnlyList<string> RootKeys { get; private init; }

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

        rawSkill ??= ReadString(root, "command_name");
        args ??= ReadArgs(root, "command_args");
        var rootKeys = root.ValueKind == JsonValueKind.Object
            ? root.EnumerateObject().Select(property => property.Name).ToList()
            : [];

        return new HookPayload
        {
            RawSkill = rawSkill,
            Args = args,
            SessionId = sessionId,
            Cwd = cwd,
            ToolInputKeys = keys,
            RootKeys = rootKeys,
        };
    }

    private static string ReadString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

    private static string ReadArgs(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
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