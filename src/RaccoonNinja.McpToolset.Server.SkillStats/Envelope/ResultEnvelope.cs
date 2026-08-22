using System.Text.Json.Serialization;
using RaccoonNinja.McpToolset.Server.SkillStats.Errors;

namespace RaccoonNinja.McpToolset.Server.SkillStats.Envelope;

/// <summary>
/// The structured envelope every tool wraps its payload in: <c>results</c>, <c>count</c>,
/// <c>filters_applied</c> (the safe echo of the shaping arguments), and <c>error</c> (null on success).
/// It carries no absolute path.
/// </summary>
public sealed record ResultEnvelope
{
    /// <summary>The result items; well-formed (empty, never null) even on failure.</summary>
    [JsonPropertyName("results")]
    public IReadOnlyList<object> Results { get; private init; } = [];

    /// <summary>The number of items in <see cref="Results"/>.</summary>
    [JsonPropertyName("count")]
    public int Count { get; private init; }

    /// <summary>The safe echo of the arguments that shaped this result.</summary>
    [JsonPropertyName("filters_applied")]
    public IDictionary<string, object> FiltersApplied { get; private init; } = new Dictionary<string, object>();

    /// <summary>The error object on failure; <c>null</c> on success.</summary>
    [JsonPropertyName("error")]
    public ErrorEnvelope Error { get; private init; }

    /// <summary>Build a success envelope from a list of result items.</summary>
    /// <param name="results">The result items.</param>
    /// <param name="filtersApplied">The safe echo of the shaping arguments.</param>
    /// <returns>The success envelope.</returns>
    public static ResultEnvelope Success(
        IReadOnlyList<object> results,
        IDictionary<string, object> filtersApplied = null)
    {
        ArgumentNullException.ThrowIfNull(results);
        return new ResultEnvelope
        {
            Results = results,
            Count = results.Count,
            FiltersApplied = filtersApplied ?? new Dictionary<string, object>(),
            Error = null,
        };
    }

    /// <summary>Build a failure envelope; <see cref="Results"/> stays a well-formed empty list.</summary>
    /// <param name="error">The domain error.</param>
    /// <param name="filtersApplied">The safe echo of the shaping arguments, if any.</param>
    /// <returns>The failure envelope.</returns>
    public static ResultEnvelope Failure(
        SkillStatsException error,
        IDictionary<string, object> filtersApplied = null)
        => new()
        {
            Results = [],
            Count = 0,
            FiltersApplied = filtersApplied ?? new Dictionary<string, object>(),
            Error = ErrorEnvelope.From(error),
        };
}