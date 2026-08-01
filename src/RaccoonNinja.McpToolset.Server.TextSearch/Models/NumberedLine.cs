using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Models;

/// <summary>One numbered line returned by <c>read_lines</c>.</summary>
/// <param name="Line">The 1-based line number.</param>
/// <param name="Text">The line's terminator-free text.</param>
public sealed record NumberedLine(
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("text")] string Text);