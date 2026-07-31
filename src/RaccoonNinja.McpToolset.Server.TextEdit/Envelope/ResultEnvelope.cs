using System.Text.Json.Serialization;
using RaccoonNinja.McpToolset.Server.TextEdit.Errors;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Envelope;

/// <summary>
/// The structured envelope every tool wraps its payload in. It carries no absolute path: every result
/// path is root-relative, and the error, when present, rides inside the payload rather than through the
/// MCP protocol error channel. A single-object result (a batch summary, a scope description) travels as a
/// one-element <see cref="Results"/> list.
/// </summary>
public sealed record ResultEnvelope
{
    /// <summary>The result items; well-formed (empty, never null) even on failure.</summary>
    [JsonPropertyName("results")]
    public IReadOnlyList<object> Results { get; private init; } = [];

    /// <summary>The number of items in <see cref="Results"/>.</summary>
    [JsonPropertyName("count")]
    public int Count { get; private init; }

    /// <summary>The safe-echo of the arguments that shaped this result (see <see cref="FiltersAppliedBuilder"/>).</summary>
    [JsonPropertyName("filters_applied")]
    public IDictionary<string, object> FiltersApplied { get; private init; } = new Dictionary<string, object>();

    /// <summary>Whether the result was capped; more may exist.</summary>
    [JsonPropertyName("truncated")]
    public bool Truncated { get; private init; }

    /// <summary>How many symlinked entries were skipped during a selector walk, when a tool reports it.</summary>
    [JsonPropertyName("skipped_symlinks")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SkippedSymlinks { get; private init; }

    /// <summary>The error object on failure; <c>null</c> on success.</summary>
    [JsonPropertyName("error")]
    public ErrorEnvelope Error { get; private init; }

    /// <summary>Build a success envelope from a list of result items.</summary>
    /// <param name="results">The result items.</param>
    /// <param name="filtersApplied">The safe-echo of the shaping arguments.</param>
    /// <param name="truncated">Whether the result was capped.</param>
    /// <param name="skippedSymlinks">The count of skipped symlinked entries, or <c>null</c> to omit.</param>
    /// <returns>The success envelope.</returns>
    public static ResultEnvelope Success(
        IReadOnlyList<object> results,
        IDictionary<string, object> filtersApplied = null,
        bool truncated = false,
        int? skippedSymlinks = null)
    {
        ArgumentNullException.ThrowIfNull(results);
        return new ResultEnvelope
        {
            Results = results,
            Count = results.Count,
            FiltersApplied = filtersApplied ?? new Dictionary<string, object>(),
            Truncated = truncated,
            SkippedSymlinks = skippedSymlinks,
            Error = null,
        };
    }

    /// <summary>Build a failure envelope; <see cref="Results"/> stays a well-formed empty list.</summary>
    /// <param name="error">The domain error.</param>
    /// <param name="filtersApplied">The safe-echo of the shaping arguments, if any.</param>
    /// <returns>The failure envelope.</returns>
    public static ResultEnvelope Failure(
        TextEditException error,
        IDictionary<string, object> filtersApplied = null)
    {
        return new ResultEnvelope
        {
            Results = [],
            Count = 0,
            FiltersApplied = filtersApplied ?? new Dictionary<string, object>(),
            Truncated = false,
            Error = ErrorEnvelope.From(error),
        };
    }
}