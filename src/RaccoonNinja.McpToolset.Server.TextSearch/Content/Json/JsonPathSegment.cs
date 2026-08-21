namespace RaccoonNinja.McpToolset.Server.TextSearch.Content.Json;

/// <summary>
/// One step of a parsed <c>json_path</c>: either a property access on an object or an index access
/// on an array. Built only through the factories so the two shapes cannot be mixed up.
/// </summary>
public sealed record JsonPathSegment
{
    /// <summary>The property name for a property segment; <c>null</c> for an index segment.</summary>
    public string Property { get; private init; }

    /// <summary>The array index for an index segment; negative counts from the end (Python style).</summary>
    public int Index { get; private init; }

    /// <summary>Whether this segment is an array index (otherwise it is a property access).</summary>
    public bool IsIndex { get; private init; }

    /// <summary>Create a property-access segment.</summary>
    /// <param name="name">The property name (may be empty when it came from a quoted form).</param>
    /// <returns>The segment.</returns>
    public static JsonPathSegment ForProperty(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return new JsonPathSegment { Property = name };
    }

    /// <summary>Create an index-access segment.</summary>
    /// <param name="index">The array index; negative counts from the end.</param>
    /// <returns>The segment.</returns>
    public static JsonPathSegment ForIndex(int index)
        => new() { Index = index, IsIndex = true };
}