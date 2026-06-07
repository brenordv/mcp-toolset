using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.GitOps.Models;

/// <summary>A unified-diff hunk with original/new offsets and line list.</summary>
public sealed record DiffHunk
{
    [JsonPropertyName("header")]
    public string Header { get; init; }

    [JsonPropertyName("old_start")]
    public int OldStart { get; init; }

    [JsonPropertyName("old_lines")]
    public int OldLines { get; init; }

    [JsonPropertyName("new_start")]
    public int NewStart { get; init; }

    [JsonPropertyName("new_lines")]
    public int NewLines { get; init; }

    [JsonPropertyName("lines")]
    public IReadOnlyList<string> Lines { get; init; } = [];
}