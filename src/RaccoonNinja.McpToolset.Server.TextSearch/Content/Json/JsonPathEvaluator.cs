using System.Text.Json.Nodes;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Content.Json;

/// <summary>
/// Walks parsed <see cref="JsonPathSegment"/>s over a document node by node. Property access matches
/// case-sensitively (Python dict parity); index access follows Python semantics, so a negative index
/// counts from the end of the array. The walk never throws: a segment that does not resolve produces
/// a miss carrying where the walk stopped.
/// </summary>
internal static class JsonPathEvaluator
{
    /// <summary>Resolve <paramref name="segments"/> against <paramref name="root"/>.</summary>
    /// <param name="root">The document root (<c>null</c> for a JSON <c>null</c> document).</param>
    /// <param name="segments">The parsed segments, root-first.</param>
    /// <returns>The hit or miss outcome.</returns>
    public static JsonPathEvaluation Evaluate(JsonNode root, IReadOnlyList<JsonPathSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var current = root;
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            if (segment.IsIndex)
            {
                if (current is not JsonArray array)
                {
                    return JsonPathEvaluation.Miss(i, current);
                }

                var effective = segment.Index < 0 ? array.Count + segment.Index : segment.Index;
                if (effective < 0 || effective >= array.Count)
                {
                    return JsonPathEvaluation.Miss(i, current);
                }

                current = array[effective];
            }
            else
            {
                if (current is not JsonObject obj || !obj.TryGetPropertyValue(segment.Property, out var child))
                {
                    return JsonPathEvaluation.Miss(i, current);
                }

                current = child;
            }
        }

        return JsonPathEvaluation.Hit(current);
    }
}