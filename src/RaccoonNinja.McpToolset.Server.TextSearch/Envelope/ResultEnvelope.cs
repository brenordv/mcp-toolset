using System.Text.Json.Serialization;
using RaccoonNinja.McpToolset.Server.TextSearch.Errors;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Envelope;

/// <summary>
/// The structured envelope every tool wraps its payload in. It carries no absolute path: every result
/// path is root-relative, and pagination is an opaque <see cref="Cursor"/> the caller passes back to
/// fetch the next page.
/// </summary>
public sealed record ResultEnvelope
{
    /// <summary>The result items; well-formed (empty, never null) even on failure.</summary>
    [JsonPropertyName("results")]
    public IReadOnlyList<object> Results { get; private init; } = [];

    /// <summary>The number of items in <see cref="Results"/>.</summary>
    [JsonPropertyName("count")]
    public int Count { get; private init; }

    /// <summary>The count before filtering, when a tool reports one.</summary>
    [JsonPropertyName("pre_filter_count")]
    public int? PreFilterCount { get; private init; }

    /// <summary>The safe-echo of the arguments that shaped this result (see <see cref="FiltersAppliedBuilder"/>).</summary>
    [JsonPropertyName("filters_applied")]
    public IDictionary<string, object> FiltersApplied { get; private init; } = new Dictionary<string, object>();

    /// <summary>Whether the result was capped; more may exist.</summary>
    [JsonPropertyName("truncated")]
    public bool Truncated { get; private init; }

    /// <summary>How many symlinked entries were skipped during the walk, when a tool reports it.</summary>
    [JsonPropertyName("skipped_symlinks")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SkippedSymlinks { get; private init; }

    /// <summary>An opaque continuation token, or <c>null</c> when there is no next page.</summary>
    [JsonPropertyName("cursor")]
    public string Cursor { get; private init; }

    /// <summary>The error object on failure; <c>null</c> on success.</summary>
    [JsonPropertyName("error")]
    public ErrorEnvelope Error { get; private init; }

    /// <summary>Build a success envelope from a list of result items.</summary>
    /// <param name="results">The result items.</param>
    /// <param name="preFilterCount">The count before filtering, if applicable.</param>
    /// <param name="filtersApplied">The safe-echo of the shaping arguments.</param>
    /// <param name="truncated">Whether the result was capped.</param>
    /// <param name="cursor">The opaque continuation token, or <c>null</c>.</param>
    /// <param name="skippedSymlinks">The count of skipped symlinked entries, or <c>null</c> to omit.</param>
    /// <returns>The success envelope.</returns>
    public static ResultEnvelope Success(
        IReadOnlyList<object> results,
        int? preFilterCount = null,
        IDictionary<string, object> filtersApplied = null,
        bool truncated = false,
        string cursor = null,
        int? skippedSymlinks = null)
    {
        ArgumentNullException.ThrowIfNull(results);
        return new ResultEnvelope
        {
            Results = results,
            Count = results.Count,
            PreFilterCount = preFilterCount,
            FiltersApplied = filtersApplied ?? new Dictionary<string, object>(),
            Truncated = truncated,
            Cursor = cursor,
            SkippedSymlinks = skippedSymlinks,
            Error = null,
        };
    }

    /// <summary>Build a failure envelope; <see cref="Results"/> stays a well-formed empty list.</summary>
    /// <param name="error">The domain error.</param>
    /// <param name="filtersApplied">The safe-echo of the shaping arguments, if any.</param>
    /// <returns>The failure envelope.</returns>
    public static ResultEnvelope Failure(
        TextSearchException error,
        IDictionary<string, object> filtersApplied = null)
    {
        return new ResultEnvelope
        {
            Results = [],
            Count = 0,
            PreFilterCount = null,
            FiltersApplied = filtersApplied ?? new Dictionary<string, object>(),
            Truncated = false,
            Cursor = null,
            Error = ErrorEnvelope.From(error),
        };
    }
}