using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Models;

/// <summary>One file's entry in a mutation result: its root-relative path, the outcome, an optional refusal reason, and an optional dry-run diff.</summary>
public sealed record FileChange
{
    /// <summary>The root-relative path.</summary>
    [JsonPropertyName("path")]
    public string Path { get; init; }

    /// <summary>The outcome: <c>changed</c>, <c>refused</c>, or <c>unchanged</c>.</summary>
    [JsonPropertyName("outcome")]
    public string Outcome { get; init; }

    /// <summary>The reason the file was refused, present only when <see cref="Outcome"/> is <c>refused</c>.</summary>
    [JsonPropertyName("refusal_reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string RefusalReason { get; init; }

    /// <summary>The unified diff of the change, present only for a <c>dry_run</c>.</summary>
    [JsonPropertyName("diff")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Diff { get; init; }
}