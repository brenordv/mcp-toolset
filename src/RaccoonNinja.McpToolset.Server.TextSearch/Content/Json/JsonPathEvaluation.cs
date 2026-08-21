using System.Text.Json.Nodes;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Content.Json;

/// <summary>
/// The outcome of walking a parsed <c>json_path</c> over a document: either the resolved value, or
/// where the walk stopped (the first segment that did not resolve and the node it was applied to),
/// so the caller can report the deepest resolved prefix and a navigation hint.
/// </summary>
public sealed record JsonPathEvaluation
{
    /// <summary>Whether every segment resolved.</summary>
    public bool Found { get; private init; }

    /// <summary>The resolved value on a hit; <c>null</c> also represents an explicit JSON <c>null</c>.</summary>
    public JsonNode Value { get; private init; }

    /// <summary>The index of the first segment that did not resolve, on a miss.</summary>
    public int FailedIndex { get; private init; }

    /// <summary>The node the failing segment was applied to (<c>null</c> when that value was JSON <c>null</c>), on a miss.</summary>
    public JsonNode FailedNode { get; private init; }

    /// <summary>A successful walk ending at <paramref name="value"/>.</summary>
    /// <param name="value">The resolved value (<c>null</c> for JSON <c>null</c>).</param>
    /// <returns>The evaluation.</returns>
    public static JsonPathEvaluation Hit(JsonNode value)
        => new() { Found = true, Value = value };

    /// <summary>A walk that stopped at segment <paramref name="failedIndex"/> applied to <paramref name="failedNode"/>.</summary>
    /// <param name="failedIndex">The index of the segment that did not resolve.</param>
    /// <param name="failedNode">The node the segment was applied to.</param>
    /// <returns>The evaluation.</returns>
    public static JsonPathEvaluation Miss(int failedIndex, JsonNode failedNode)
        => new() { FailedIndex = failedIndex, FailedNode = failedNode };
}