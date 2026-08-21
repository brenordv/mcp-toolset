using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Models;

/// <summary>
/// The single result returned by <c>read_json</c>: the value at the requested <c>json_path</c>,
/// embedded as real JSON so the caller never re-parses a double-encoded string.
/// </summary>
/// <param name="Path">The confined scope-relative path of the file that was read.</param>
/// <param name="JsonPath">The <c>json_path</c> argument, echoed verbatim; <c>null</c> when the whole document was requested.</param>
/// <param name="Kind">The value's kind: <c>object</c>, <c>array</c>, <c>string</c>, <c>number</c>, <c>boolean</c>, or <c>null</c>.</param>
/// <param name="Value">The resolved JSON value, serialized in place; JSON <c>null</c> for kind <c>null</c>.</param>
public sealed record JsonValueResult(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("json_path")] string JsonPath,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("value")] JsonNode Value);