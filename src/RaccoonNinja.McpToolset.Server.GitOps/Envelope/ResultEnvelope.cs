using System.Text.Json.Serialization;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;

namespace RaccoonNinja.McpToolset.Server.GitOps.Envelope;

/// <summary>
/// The structured envelope every collection-returning tool wraps its payload in.
/// Carries enough metadata for the AI to interpret what came back without
/// re-issuing the underlying git command.
/// </summary>
public sealed record ResultEnvelope
{
    [JsonPropertyName("results")]
    public IReadOnlyList<object> Results { get; private init; } = [];

    [JsonPropertyName("count")]
    public int Count { get; private init; }

    [JsonPropertyName("pre_filter_count")]
    public int? PreFilterCount { get; private init; }

    [JsonPropertyName("filters_applied")]
    public IDictionary<string, object> FiltersApplied { get; private init; } = new Dictionary<string, object>();

    [JsonPropertyName("truncated")]
    public bool Truncated { get; private init; }

    [JsonPropertyName("repo_root")]
    public string RepoRoot { get; private init; }

    [JsonPropertyName("error")]
    public ErrorEnvelope Error { get; private init; }

    /// <summary>Build a success envelope from a list of result items.</summary>
    public static ResultEnvelope Success(
        IReadOnlyList<object> results,
        string repoRoot,
        int? preFilterCount = null,
        IDictionary<string, object> filtersApplied = null,
        bool truncated = false)
    {
        return new ResultEnvelope
        {
            Results = results,
            Count = results.Count,
            PreFilterCount = preFilterCount,
            FiltersApplied = filtersApplied ?? new Dictionary<string, object>(),
            Truncated = truncated,
            RepoRoot = repoRoot,
            Error = null,
        };
    }

    /// <summary>Build a failure envelope; <see cref="Results"/> stays well-formed (empty list).</summary>
    public static ResultEnvelope Failure(
        GitCheckException error,
        string repoRoot = null,
        IDictionary<string, object> filtersApplied = null)
    {
        return new ResultEnvelope
        {
            Results = [],
            Count = 0,
            PreFilterCount = null,
            FiltersApplied = filtersApplied ?? new Dictionary<string, object>(),
            Truncated = false,
            RepoRoot = repoRoot,
            Error = ErrorEnvelope.From(error),
        };
    }
}