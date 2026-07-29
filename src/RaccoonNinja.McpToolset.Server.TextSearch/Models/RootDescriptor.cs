using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Models;

/// <summary>One root as reported by <c>describe_scope</c>: its agent-facing name and kind.</summary>
/// <param name="Name">The name the agent targets the root by.</param>
/// <param name="Kind">The root kind: <c>workspace</c> or <c>package</c>.</param>
public sealed record RootDescriptor(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("kind")] string Kind);