using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.SkillStats.Models;

/// <summary>
/// The single <c>usage_summary</c> row: store totals plus ingestion-health fields that distinguish
/// "the hook never fired" (no records, no error log) from "the hook fires but fails" (an error log
/// with recent writes).
/// </summary>
public sealed record UsageSummaryResult
{
    /// <summary>Total recorded invocations across all skills.</summary>
    [JsonPropertyName("total_records")]
    public long TotalRecords { get; init; }

    /// <summary>How many distinct skills have been recorded.</summary>
    [JsonPropertyName("distinct_skills")]
    public long DistinctSkills { get; init; }

    /// <summary>The earliest recorded use, formatted UTC, or <c>null</c> when the store is empty.</summary>
    [JsonPropertyName("first_used")]
    public string FirstUsed { get; init; }

    /// <summary>The latest recorded use, formatted UTC, or <c>null</c> when the store is empty.</summary>
    [JsonPropertyName("last_used")]
    public string LastUsed { get; init; }

    /// <summary>The store schema version.</summary>
    [JsonPropertyName("schema_version")]
    public long SchemaVersion { get; init; }

    /// <summary>The database file size in bytes.</summary>
    [JsonPropertyName("db_size_bytes")]
    public long DbSizeBytes { get; init; }

    /// <summary>Whether <c>SKILL_STATS_HOME</c> was set (a split-store misconfiguration signal).</summary>
    [JsonPropertyName("home_overridden")]
    public bool HomeOverridden { get; init; }

    /// <summary>The ingest error log size in bytes, or 0 when absent.</summary>
    [JsonPropertyName("error_log_size_bytes")]
    public long ErrorLogSizeBytes { get; init; }

    /// <summary>The ingest error log's last-write time, formatted UTC, or <c>null</c> when absent.</summary>
    [JsonPropertyName("error_log_last_write")]
    public string ErrorLogLastWrite { get; init; }
}