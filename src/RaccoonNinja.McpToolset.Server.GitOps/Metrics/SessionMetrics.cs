using System.Collections.Concurrent;

namespace RaccoonNinja.McpToolset.Server.GitOps.Metrics;

/// <summary>
/// Per-process metrics aggregator. Counters use <see cref="Interlocked"/>;
/// duration ring buffer is guarded by a lock. Emitted as a single
/// <c>metrics_summary</c> on the <c>server_stop</c> log record at shutdown.
/// </summary>
public sealed class SessionMetrics
{
    private const int DurationRingSize = 10_000;

    private readonly ConcurrentDictionary<string, long> _toolCalls = new(StringComparer.Ordinal);
    private long _timeouts;
    private long _gitCommandErrors;
    private long _truncations;
    private long _cacheHits;
    private long _cacheMisses;
    private readonly Lock _durationsLock = new();
    private readonly Queue<int> _durationsMs = new(DurationRingSize);

    public void RecordToolCall(string tool, string outcome)
    {
        var key = $"{tool}:{outcome}";
        _toolCalls.AddOrUpdate(key, 1, (_, current) => current + 1);
    }

    public void RecordTimeout() => Interlocked.Increment(ref _timeouts);

    public void RecordGitCommandError() => Interlocked.Increment(ref _gitCommandErrors);

    public void RecordTruncation() => Interlocked.Increment(ref _truncations);

    public void RecordCache(bool hit)
    {
        if (hit) Interlocked.Increment(ref _cacheHits);
        else Interlocked.Increment(ref _cacheMisses);
    }

    public void RecordDurationMs(int value)
    {
        if (value < 0) return;
        lock (_durationsLock)
        {
            if (_durationsMs.Count >= DurationRingSize) _durationsMs.Dequeue();
            _durationsMs.Enqueue(value);
        }
    }

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
            ["subprocess_duration_ms_p50"] = p50,
            ["subprocess_duration_ms_p95"] = p95,
            ["timeouts_total"] = Interlocked.Read(ref _timeouts),
            ["git_command_errors_total"] = Interlocked.Read(ref _gitCommandErrors),
            ["truncations_total"] = Interlocked.Read(ref _truncations),
            ["cache_hits_total"] = Interlocked.Read(ref _cacheHits),
            ["cache_misses_total"] = Interlocked.Read(ref _cacheMisses),
        };
    }

    private static (int P50, int P95) Quantiles(IEnumerable<int> samples)
    {
        var ordered = samples.OrderBy(x => x).ToArray();
        return ordered.Length switch
        {
            0 => (0, 0),
            1 => (ordered[0], ordered[0]),
            _ => (Percentile(ordered, 50), Percentile(ordered, 95))
        };
    }

    private static int Percentile(int[] sortedSamples, int percent)
    {
        // Linear-interpolation percentile (NIST primary definition).
        var rank = (percent / 100.0) * (sortedSamples.Length - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        if (lower == upper) return sortedSamples[lower];
        var weight = rank - lower;
        return (int)(sortedSamples[lower] + weight * (sortedSamples[upper] - sortedSamples[lower]));
    }
}