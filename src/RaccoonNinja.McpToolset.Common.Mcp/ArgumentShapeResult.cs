namespace RaccoonNinja.McpToolset.Common.Mcp;

/// <summary>
/// The outcome of validating a call's argument names against a tool's input schema: whether the names
/// are well-formed, plus the pieces a failure envelope is built from.
/// </summary>
public sealed record ArgumentShapeResult
{
    /// <summary>Whether every supplied name is a schema property and every required name is present.</summary>
    public bool IsValid { get; init; }

    /// <summary>The required schema properties the call did not supply.</summary>
    public IReadOnlyList<string> MissingRequired { get; init; } = [];

    /// <summary>The supplied names that are not schema properties (capped and sanitized), each with its suggestion.</summary>
    public IReadOnlyList<ArgumentShapeUnknown> Unknown { get; init; } = [];

    /// <summary>Every schema property name, in schema order; the caller's full expected-name list.</summary>
    public IReadOnlyList<string> Expected { get; init; } = [];
}