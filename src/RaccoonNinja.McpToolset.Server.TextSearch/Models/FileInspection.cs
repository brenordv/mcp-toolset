using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Models;

/// <summary>One entry returned by <c>inspect_files</c>: the encoding and text shape of a file.</summary>
public sealed record FileInspection : IHasPath
{
    /// <summary>The <c>/</c>-separated path relative to the call's scope (the <c>cwd</c>, or the base root when it is omitted).</summary>
    [JsonPropertyName("path")]
    public string Path { get; init; }

    /// <summary>The detected encoding name (for example <c>utf-8</c>, <c>utf-16le</c>, <c>windows-1252</c>).</summary>
    [JsonPropertyName("encoding")]
    public string Encoding { get; init; }

    /// <summary>The detector confidence in the range 0 to 1.</summary>
    [JsonPropertyName("encoding_confidence")]
    public double EncodingConfidence { get; init; }

    /// <summary>Whether the file began with a byte-order mark.</summary>
    [JsonPropertyName("has_bom")]
    public bool HasBom { get; init; }

    /// <summary>The line-ending style: <c>none</c>, <c>lf</c>, <c>crlf</c>, <c>cr</c>, or <c>mixed</c>.</summary>
    [JsonPropertyName("line_endings")]
    public string LineEndings { get; init; }

    /// <summary>Whether the file ends with a line terminator.</summary>
    [JsonPropertyName("final_newline")]
    public bool FinalNewline { get; init; }

    /// <summary>How many lines carry trailing whitespace.</summary>
    [JsonPropertyName("trailing_whitespace_lines")]
    public int TrailingWhitespaceLines { get; init; }

    /// <summary>The number of lines; <c>0</c> for a binary file.</summary>
    [JsonPropertyName("line_count")]
    public int LineCount { get; init; }

    /// <summary>The file size in bytes.</summary>
    [JsonPropertyName("size")]
    public long Size { get; init; }

    /// <summary>Whether the file was classified as binary and not decoded.</summary>
    [JsonPropertyName("is_binary")]
    public bool IsBinary { get; init; }
}