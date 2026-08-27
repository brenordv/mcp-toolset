namespace RaccoonNinja.McpToolset.Common.Mcp;

/// <summary>
/// One unknown argument the caller supplied: the sanitized name as given, and the closest schema
/// argument name, or <c>null</c> when nothing in the schema is close enough to suggest.
/// </summary>
/// <param name="Given">The caller's argument name, control-stripped and length-capped for safe echo.</param>
/// <param name="DidYouMean">The closest schema argument name, or <c>null</c> when there is no close match.</param>
public sealed record ArgumentShapeUnknown(string Given, string DidYouMean);