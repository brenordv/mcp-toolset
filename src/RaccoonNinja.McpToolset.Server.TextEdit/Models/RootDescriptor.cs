using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Models;

/// <summary>One root as reported by <c>describe_scope</c>: its agent-facing name and kind.</summary>
/// <param name="Name">The name the agent targets the root by.</param>
/// <param name="Kind">The root kind; always <c>workspace</c> for this server.</param>
public sealed record RootDescriptor(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("kind")] string Kind);