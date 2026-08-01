using RaccoonNinja.McpToolset.Server.GitOps.Metrics;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Metrics;

public class SessionMetricsTests
{
    [Fact]
    public void Summary_IsEmptyForFreshInstance()
    {
        // Arrange
        var metrics = new SessionMetrics();

        // Act
        var summary = metrics.Summary();

        // Assert
        Assert.Equal(0, summary["subprocess_duration_ms_p50"]);
        Assert.Equal(0, summary["subprocess_duration_ms_p95"]);
        Assert.Equal(0L, summary["cache_hits_total"]);
    }

    [Fact]
    public void Counters_AccumulatePerToolAndOutcome()
    {
        // Arrange
        var metrics = new SessionMetrics();
        metrics.RecordToolCall("git_log", "ok");
        metrics.RecordToolCall("git_log", "ok");
        metrics.RecordToolCall("git_log", "error");
        metrics.RecordCache(true);
        metrics.RecordCache(false);
        metrics.RecordTruncation();
        metrics.RecordTimeout();

        // Act
        var summary = metrics.Summary();

        // Assert
        var calls = (System.Collections.Generic.Dictionary<string, object>)summary["tool_calls_total"];
        Assert.Equal(2L, (long)calls["git_log:ok"]);
        Assert.Equal(1L, (long)calls["git_log:error"]);
        Assert.Equal(1L, summary["cache_hits_total"]);
        Assert.Equal(1L, summary["cache_misses_total"]);
        Assert.Equal(1L, summary["truncations_total"]);
        Assert.Equal(1L, summary["timeouts_total"]);
    }

    [Fact]
    public void Git_CommandErrorsAreCountedSeparatelyFromTimeouts()
    {
        // Arrange
        var metrics = new SessionMetrics();
        metrics.RecordGitCommandError();
        metrics.RecordGitCommandError();
        metrics.RecordTimeout();

        // Act
        var summary = metrics.Summary();

        // Assert
        Assert.Equal(2L, summary["git_command_errors_total"]);
        Assert.Equal(1L, summary["timeouts_total"]);
    }

    [Fact]
    public void Duration_QuantilesTrackMinAndMaxSample()
    {
        // Arrange
        var metrics = new SessionMetrics();
        for (var i = 1; i <= 100; i++) metrics.RecordDurationMs(i);

        // Act
        var summary = metrics.Summary();

        // Assert
        var p50 = (int)summary["subprocess_duration_ms_p50"];
        var p95 = (int)summary["subprocess_duration_ms_p95"];
        Assert.InRange(p50, 40, 60);
        Assert.InRange(p95, 90, 100);
    }
}