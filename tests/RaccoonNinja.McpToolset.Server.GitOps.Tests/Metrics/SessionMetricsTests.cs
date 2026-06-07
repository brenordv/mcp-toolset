using RaccoonNinja.McpToolset.Server.GitOps.Metrics;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Metrics;

public class SessionMetricsTests
{
    [Fact]
    public void Summary_Is_Empty_For_Fresh_Instance()
    {
        var metrics = new SessionMetrics();
        var summary = metrics.Summary();
        Assert.Equal(0, summary["subprocess_duration_ms_p50"]);
        Assert.Equal(0, summary["subprocess_duration_ms_p95"]);
        Assert.Equal(0L, summary["cache_hits_total"]);
    }

    [Fact]
    public void Counters_Accumulate_Per_Tool_And_Outcome()
    {
        var metrics = new SessionMetrics();
        metrics.RecordToolCall("git_log", "ok");
        metrics.RecordToolCall("git_log", "ok");
        metrics.RecordToolCall("git_log", "error");
        metrics.RecordCache(true);
        metrics.RecordCache(false);
        metrics.RecordTruncation();
        metrics.RecordTimeout();

        var summary = metrics.Summary();
        var calls = (System.Collections.Generic.Dictionary<string, object>)summary["tool_calls_total"];
        Assert.Equal(2L, (long)calls["git_log:ok"]);
        Assert.Equal(1L, (long)calls["git_log:error"]);
        Assert.Equal(1L, summary["cache_hits_total"]);
        Assert.Equal(1L, summary["cache_misses_total"]);
        Assert.Equal(1L, summary["truncations_total"]);
        Assert.Equal(1L, summary["timeouts_total"]);
    }

    [Fact]
    public void Git_Command_Errors_Are_Counted_Separately_From_Timeouts()
    {
        var metrics = new SessionMetrics();
        metrics.RecordGitCommandError();
        metrics.RecordGitCommandError();
        metrics.RecordTimeout();

        var summary = metrics.Summary();
        Assert.Equal(2L, summary["git_command_errors_total"]);
        Assert.Equal(1L, summary["timeouts_total"]);
    }

    [Fact]
    public void Duration_Quantiles_Track_Min_And_Max_Sample()
    {
        var metrics = new SessionMetrics();
        for (var i = 1; i <= 100; i++) metrics.RecordDurationMs(i);
        var summary = metrics.Summary();
        var p50 = (int)summary["subprocess_duration_ms_p50"];
        var p95 = (int)summary["subprocess_duration_ms_p95"];
        Assert.InRange(p50, 40, 60);
        Assert.InRange(p95, 90, 100);
    }
}