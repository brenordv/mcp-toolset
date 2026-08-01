using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Content;

/// <summary>
/// One search hit, and the wire shape <c>search_text</c> returns. Offsets are line-relative UTF-16
/// code units: <see cref="Column"/> is 1-based, <see cref="MatchStart"/>/<see cref="MatchEnd"/> are
/// 0-based into the line's text. The path is root-relative.
/// </summary>
public sealed record ContentMatch
{
    /// <summary>The name of the root the file is in.</summary>
    [JsonPropertyName("root")]
    public string Root { get; init; }

    /// <summary>The path relative to <see cref="Root"/> of the file the match is in.</summary>
    [JsonPropertyName("path")]
    public string Path { get; init; }

    /// <summary>The 1-based line number.</summary>
    [JsonPropertyName("line")]
    public int Line { get; init; }

    /// <summary>The 1-based UTF-16 column of the match start.</summary>
    [JsonPropertyName("column")]
    public int Column { get; init; }

    /// <summary>The matched line's text (terminator-free, capped for very long lines).</summary>
    [JsonPropertyName("text")]
    public string Text { get; init; }

    /// <summary>The 0-based UTF-16 start offset of the match within the line.</summary>
    [JsonPropertyName("match_start")]
    public int MatchStart { get; init; }

    /// <summary>The 0-based UTF-16 end offset (exclusive) of the match within the line.</summary>
    [JsonPropertyName("match_end")]
    public int MatchEnd { get; init; }

    /// <summary>The lines before the match, when context was requested; otherwise omitted.</summary>
    [JsonPropertyName("before")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ContextLine> Before { get; init; }

    /// <summary>The lines after the match, when context was requested; otherwise omitted.</summary>
    [JsonPropertyName("after")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ContextLine> After { get; init; }
}