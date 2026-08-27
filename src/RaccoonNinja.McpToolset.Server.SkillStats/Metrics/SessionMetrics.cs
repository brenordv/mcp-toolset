using System.Collections.Concurrent;

namespace RaccoonNinja.McpToolset.Server.SkillStats.Metrics;

/// <summary>
/// Per-process metrics aggregator. Counts tool calls by outcome (<c>ok</c>, <c>invalid_argument</c>,
/// <c>store_unavailable</c>, <c>internal_error</c>, <c>binding_error</c>), emitted as a single
/// <c>metrics_summary</c> on the <c>server_stop</c> record at shutdown.
/// </summary>
public sealed class SessionMetrics
{
    private readonly ConcurrentDictionary<string, long> _toolCalls = new(StringComparer.Ordinal);

    /// <summary>Record one completed tool call and its outcome.</summary>
    /// <param name="tool">The tool name.</param>
    /// <param name="outcome">The call outcome (<c>ok</c>, <c>invalid_argument</c>, <c>store_unavailable</c>, <c>internal_error</c>, <c>binding_error</c>).</param>
    public void RecordToolCall(string tool, string outcome)
        => _toolCalls.AddOrUpdate($"{tool}:{outcome}", 1, static (_, current) => current + 1);

    /// <summary>Snapshot the counters as a summary dictionary for the shutdown log record.</summary>
    /// <returns>The metrics snapshot.</returns>
    public IDictionary<string, object> Summary()
        => new Dictionary<string, object>
        {
            ["tool_calls_total"] = _toolCalls.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value),
        };
}