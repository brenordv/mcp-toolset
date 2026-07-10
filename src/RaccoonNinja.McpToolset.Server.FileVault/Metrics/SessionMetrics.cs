using System.Collections.Concurrent;

namespace RaccoonNinja.McpToolset.Server.FileVault.Metrics;

/// <summary>
/// Per-process metrics aggregator, vault-shaped: per-tool call counters keyed by outcome or
/// domain error code (e.g. <c>vault_save:conflict</c>), an internal-error counter, migration
/// count, and a duration ring for p50/p95. Emitted as one <c>metrics_summary</c> on the
/// <c>server_stop</c> record at shutdown.
/// </summary>
public sealed class SessionMetrics
{
    private const int DurationRingSize = 10_000;

    private readonly ConcurrentDictionary<string, long> _toolCalls = new(StringComparer.Ordinal);
    private long _internalErrors;
    private long _migrationsApplied;
    private readonly Lock _durationsLock = new();
    private readonly Queue<int> _durationsMs = new(DurationRingSize);

    /// <summary>Record one tool call with its outcome (<c>ok</c> or a domain error code).</summary>
    /// <param name="tool">The tool name.</param>
    /// <param name="outcome">The outcome key.</param>
    public void RecordToolCall(string tool, string outcome)
    {
        var key = $"{tool}:{outcome}";
        _toolCalls.AddOrUpdate(key, 1, (_, current) => current + 1);
    }

    /// <summary>Record one internal (non-domain) failure.</summary>
    public void RecordInternalError() => Interlocked.Increment(ref _internalErrors);

    /// <summary>Record the number of migrations applied at startup.</summary>
    /// <param name="count">The migration count.</param>
    public void RecordMigrationsApplied(int count) => Interlocked.Add(ref _migrationsApplied, count);

    /// <summary>Record one tool-call duration sample.</summary>
    /// <param name="value">The duration in milliseconds.</param>
    public void RecordDurationMs(int value)
    {
        if (value < 0)
        {
            return;
        }

        lock (_durationsLock)
        {
            if (_durationsMs.Count >= DurationRingSize)
            {
                _durationsMs.Dequeue();
            }

            _durationsMs.Enqueue(value);
        }
    }

    /// <summary>Build the shutdown snapshot.</summary>
    /// <returns>The metrics summary map.</returns>
    public IDictionary<string, object> Summary()
    {
        int p50;
        int p95;
        lock (_durationsLock)
        {
            (p50, p95) = Quantiles(_durationsMs);
        }

        return new Dictionary<string, object>
        {
            ["tool_calls_total"] = _toolCalls.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value),
            ["call_duration_ms_p50"] = p50,
            ["call_duration_ms_p95"] = p95,
            ["internal_errors_total"] = Interlocked.Read(ref _internalErrors),
            ["migrations_applied"] = Interlocked.Read(ref _migrationsApplied),
        };
    }

    private static (int P50, int P95) Quantiles(IEnumerable<int> samples)
    {
        var ordered = samples.OrderBy(x => x).ToArray();
        return ordered.Length switch
        {
            0 => (0, 0),
            1 => (ordered[0], ordered[0]),
            _ => (Percentile(ordered, 50), Percentile(ordered, 95)),
        };
    }

    private static int Percentile(int[] sortedSamples, int percent)
    {
        // Linear-interpolation percentile (NIST primary definition).
        var rank = (percent / 100.0) * (sortedSamples.Length - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        if (lower == upper)
        {
            return sortedSamples[lower];
        }

        var weight = rank - lower;
        return (int)(sortedSamples[lower] + weight * (sortedSamples[upper] - sortedSamples[lower]));
    }
}