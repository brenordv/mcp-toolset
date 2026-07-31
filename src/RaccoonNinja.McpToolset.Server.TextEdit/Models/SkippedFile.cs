using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Models;

/// <summary>One file an undo declined to restore, with the reason.</summary>
public sealed record SkippedFile
{
    /// <summary>The root-relative path that was skipped.</summary>
    [JsonPropertyName("path")]
    public string Path { get; init; }

    /// <summary>Why it was skipped (<c>out_of_root</c>, <c>denied</c>, <c>hash_mismatch</c>, or <c>io_error</c>).</summary>
    [JsonPropertyName("reason")]
    public string Reason { get; init; }
}