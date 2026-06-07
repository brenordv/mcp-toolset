using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.GitOps.Models;

/// <summary>
/// The kind of change a <see cref="FileDiff"/> represents. Serialized as a lowercase
/// string (e.g. <c>"modified"</c>) via <see cref="JsonStringEnumConverter{TEnum}"/> so the
/// wire contract stays human- and tool-readable. <see cref="Unknown"/> is used when the
/// change kind cannot be determined from the available git output (e.g. <c>--numstat</c>,
/// which carries counts but no type marker).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ChangeType>))]
public enum ChangeType
{
    [JsonStringEnumMemberName("unknown")] Unknown = 0,
    [JsonStringEnumMemberName("modified")] Modified = 1,
    [JsonStringEnumMemberName("added")] Added = 2,
    [JsonStringEnumMemberName("deleted")] Deleted = 3,
    [JsonStringEnumMemberName("renamed")] Renamed = 4,
    [JsonStringEnumMemberName("copied")] Copied = 5
}