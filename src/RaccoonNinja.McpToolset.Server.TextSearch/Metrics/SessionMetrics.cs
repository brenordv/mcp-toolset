using System.Collections.Concurrent;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Metrics;

/// <summary>
/// Per-process metrics aggregator. Counters use <see cref="Interlocked"/>; the duration ring buffer is
/// lock-guarded. Emitted as a single <c>metrics_summary</c> on the <c>server_stop</c> record at
/// shutdown. A spike in refusals is the signal that something is probing the boundary.
/// </summary>
public sealed class SessionMetrics
{
    private const int DurationRingSize = 10_000;

    private readonly ConcurrentDictionary<string, long> _toolCalls = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _refusals = new(StringComparer.Ordinal);
    private readonly Lock _durationsLock = new();
    private readonly Queue<int> _durationsMs = new(DurationRingSize);
    private long _regexTimeouts;
    private long _regexFallbacks;
    private long _truncations;
    private long _wholeBaseCalls;
    private long _packageRootCalls;
    private long _includeIgnoredCalls;

    /// <summary>Record one completed tool call and its outcome (<c>ok</c> or <c>error</c>).</summary>
    /// <param name="tool">The tool name.</param>
    /// <param name="outcome">The call outcome.</param>
    public void RecordToolCall(string tool, string outcome)
        => _toolCalls.AddOrUpdate($"{tool}:{outcome}", 1, static (_, current) => current + 1);

    /// <summary>Record one refusal, keyed by reason (for example <c>denylisted</c>, <c>out_of_root</c>, <c>binary</c>).</summary>
    /// <param name="reason">The refusal reason.</param>
    public void RecordRefusal(string reason)
        => _refusals.AddOrUpdate(reason, 1, static (_, current) => current + 1);

    /// <summary>Record one regex match-timeout hit.</summary>
    public void RecordRegexTimeout() => Interlocked.Increment(ref _regexTimeouts);

    /// <summary>Record one non-backtracking-to-backtracking regex fallback.</summary>
    public void RecordRegexFallback() => Interlocked.Increment(ref _regexFallbacks);

    /// <summary>Record one truncated (capped) result.</summary>
    public void RecordTruncation() => Interlocked.Increment(ref _truncations);

    /// <summary>Record one call that searched the whole base root (no <c>cwd</c> scope, the heavy walk).</summary>
    public void RecordWholeBaseCall() => Interlocked.Increment(ref _wholeBaseCalls);

    /// <summary>Record one call that targeted a package root (an <c>@name</c> <c>cwd</c>, an out-of-tree cache).</summary>
    public void RecordPackageRootCall() => Interlocked.Increment(ref _packageRootCalls);

    /// <summary>Record one call that re-included otherwise-ignored paths (the "reaching into hidden files" signal).</summary>
    public void RecordIncludeIgnored() => Interlocked.Increment(ref _includeIgnoredCalls);

    /// <summary>Record one operation's wall-clock duration in milliseconds.</summary>
    /// <param name="value">The duration; negatives are ignored.</param>
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

    /// <summary>Snapshot the counters as a summary dictionary for the shutdown log record.</summary>
    /// <returns>The metrics snapshot.</returns>
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
            ["refusals_total"] = _refusals.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value),
            ["op_duration_ms_p50"] = p50,
            ["op_duration_ms_p95"] = p95,
            ["regex_timeouts_total"] = Interlocked.Read(ref _regexTimeouts),
            ["regex_fallbacks_total"] = Interlocked.Read(ref _regexFallbacks),
            ["truncations_total"] = Interlocked.Read(ref _truncations),
            ["whole_base_calls_total"] = Interlocked.Read(ref _wholeBaseCalls),
            ["package_root_calls_total"] = Interlocked.Read(ref _packageRootCalls),
            ["include_ignored_calls_total"] = Interlocked.Read(ref _includeIgnoredCalls),
        };
    }

    private static (int P50, int P95) Quantiles(IEnumerable<int> samples)
    {
        var ordered = samples.OrderBy(static x => x).ToArray();
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
        return (int)(sortedSamples[lower] + (weight * (sortedSamples[upper] - sortedSamples[lower])));
    }
}