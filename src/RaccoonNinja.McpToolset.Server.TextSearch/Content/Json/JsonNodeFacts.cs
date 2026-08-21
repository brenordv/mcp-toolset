using System.Text.Json;
using System.Text.Json.Nodes;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Content.Json;

/// <summary>
/// Describes a <see cref="JsonNode"/> for the wire: the <c>kind</c> string the result payload
/// carries, and the capped navigation hint (property names or array length) that error detail
/// carries so an agent can narrow a missed or oversized <c>json_path</c> in one round trip. Hints
/// are file content, so they are size-capped and must only ever travel in error <c>detail</c>,
/// never in an error message (messages are logged verbatim).
/// </summary>
internal static class JsonNodeFacts
{
    /// <summary>The maximum number of property names emitted in a hint.</summary>
    internal const int MaxHintProperties = 50;

    /// <summary>The maximum length of one emitted property name; longer names are truncated with a marker.</summary>
    internal const int MaxHintPropertyLength = 100;

    private const string TruncationMarker = "...";

    /// <summary>The wire <c>kind</c> of <paramref name="node"/>; JSON <c>true</c>/<c>false</c> both map to <c>boolean</c>.</summary>
    /// <param name="node">The node (<c>null</c> means JSON <c>null</c>).</param>
    /// <returns>
    /// One of <c>object</c>, <c>array</c>, <c>string</c>, <c>number</c>, <c>boolean</c>, <c>null</c>.
    /// The <c>undefined</c> arm is a defensive fallback a parsed document cannot reach.
    /// </returns>
    public static string KindOf(JsonNode node)
        => node is null
            ? "null"
            : node.GetValueKind() switch
            {
                JsonValueKind.Object => "object",
                JsonValueKind.Array => "array",
                JsonValueKind.String => "string",
                JsonValueKind.Number => "number",
                JsonValueKind.True or JsonValueKind.False => "boolean",
                JsonValueKind.Null => "null",
                _ => "undefined",
            };

    /// <summary>
    /// Add the navigation hint for <paramref name="node"/> to <paramref name="detail"/>: the capped
    /// property names and total count for an object, the length for an array, nothing for a scalar
    /// (its <c>kind</c> already says everything).
    /// </summary>
    /// <param name="detail">The error-detail map to add to.</param>
    /// <param name="node">The node to describe.</param>
    public static void AddHint(IDictionary<string, object> detail, JsonNode node)
    {
        ArgumentNullException.ThrowIfNull(detail);

        switch (node)
        {
            case JsonObject obj:
                detail["property_count"] = obj.Count;
                detail["properties"] = obj
                    .Take(MaxHintProperties)
                    .Select(property => Truncate(property.Key))
                    .ToArray();
                break;
            case JsonArray array:
                detail["length"] = array.Count;
                break;
        }
    }

    /// <summary>Truncate <paramref name="name"/> to the hint cap with a marker.</summary>
    private static string Truncate(string name)
        => name.Length <= MaxHintPropertyLength ? name : string.Concat(name[..MaxHintPropertyLength], TruncationMarker);
}