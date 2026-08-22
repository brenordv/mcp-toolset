using System.ComponentModel;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.SkillStats.Envelope;
using RaccoonNinja.McpToolset.Server.SkillStats.Models;
using RaccoonNinja.McpToolset.Shared.SkillStats.Configuration;
using RaccoonNinja.McpToolset.Shared.SkillStats.Storage;

namespace RaccoonNinja.McpToolset.Server.SkillStats.Tools;

/// <summary>The <c>usage_summary</c> tool: store-wide totals and ingestion health.</summary>
[McpServerToolType]
public sealed class UsageSummaryTool(ToolCommon common, SkillUsageRepository repository, SkillStatsConfig config)
{
    /// <summary>Return store-wide totals plus ingestion-health fields.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An envelope carrying the single summary row.</returns>
    [McpServerTool(Name = "usage_summary", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description(
        "Store-wide totals: total records, distinct skills, first and last recorded use, schema version, "
        + "and database size. It also reports ingestion health: whether SKILL_STATS_HOME is set, and the "
        + "ingest error log's size and last-write time, so \"the hook never fired\" and \"the hook fires "
        + "but fails\" are distinguishable.")]
    public Task<ResultEnvelope> InvokeAsync(CancellationToken cancellationToken = default)
    {
        var ctx = common.MakeContext("usage_summary");
        return common.WrapAsync(ctx, () =>
        {
            common.GuardStore();

            var summary = repository.Summary();
            var databaseInfo = new FileInfo(config.DbPath);
            var errorLogInfo = new FileInfo(config.ErrorLogPath);

            var result = new UsageSummaryResult
            {
                TotalRecords = summary.TotalRecords,
                DistinctSkills = summary.DistinctSkills,
                FirstUsed = ToolCommon.FormatTimestamp(summary.FirstUsed),
                LastUsed = ToolCommon.FormatTimestamp(summary.LastUsed),
                SchemaVersion = repository.SchemaVersion(),
                DbSizeBytes = databaseInfo.Exists ? databaseInfo.Length : 0,
                HomeOverridden = config.HomeOverridden,
                ErrorLogSizeBytes = errorLogInfo.Exists ? errorLogInfo.Length : 0,
                ErrorLogLastWrite = errorLogInfo.Exists
                    ? ToolCommon.FormatTimestamp(((DateTimeOffset)errorLogInfo.LastWriteTimeUtc).ToUnixTimeSeconds())
                    : null,
            };

            return Task.FromResult(ResultEnvelope.Success([result]));
        });
    }
}