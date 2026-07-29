using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Content;

/// <summary>A context line around a match: its 1-based number and (capped) text.</summary>
/// <param name="Line">The 1-based line number.</param>
/// <param name="Text">The line's terminator-free text.</param>
public sealed record ContextLine(
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("text")] string Text);