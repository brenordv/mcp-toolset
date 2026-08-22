using System.ComponentModel;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.SkillStats.Envelope;
using RaccoonNinja.McpToolset.Server.SkillStats.Models;
using RaccoonNinja.McpToolset.Common.SkillStats.Storage;

namespace RaccoonNinja.McpToolset.Server.SkillStats.Tools;

/// <summary>The <c>top_skills</c> tool: the most-used skills over an optional recent window.</summary>
[McpServerToolType]
public sealed class TopSkillsTool(ToolCommon common, SkillUsageRepository repository)
{
    private const int DefaultLimit = 20;
    private const int MaxLimit = 100;

    /// <summary>Return the most-used skills, ordered by use count then name.</summary>
    /// <param name="days_back">Restrict to uses in the last N days (1..3650); omit for all time.</param>
    /// <param name="limit">The maximum number of skills to return (1..100).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An envelope of skill counts.</returns>
    [McpServerTool(Name = "top_skills", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description(
        "The most-used skills, most first. Pass days_back to restrict to the last N days (1-3650); omit "
        + "it for all time. limit caps the number of skills returned (default 20, max 100). Each row has "
        + "the skill name, its use count, and the first and last use in the window.")]
    public Task<ResultEnvelope> InvokeAsync(
        [Description("Restrict to uses in the last N days (1-3650). Omit for all time.")]
        int? days_back = null,
        [Description("The maximum number of skills to return (1-100). Default 20.")]
        int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var ctx = common.MakeContext("top_skills");
        return common.WrapAsync(ctx, () =>
        {
            ToolCommon.ValidateLimit(limit, MaxLimit);
            var cutoff = ToolCommon.ResolveCutoff(days_back);
            common.GuardStore();

            var rows = repository.TopSkills(cutoff, limit)
                .Select(static row => (object)new TopSkillRow
                {
                    Skill = row.Skill,
                    Uses = row.Uses,
                    FirstUsed = ToolCommon.FormatTimestamp(row.FirstUsed),
                    LastUsed = ToolCommon.FormatTimestamp(row.LastUsed),
                })
                .ToList();

            var filters = FiltersAppliedBuilder.Create();
            if (days_back.HasValue)
            {
                filters.Number("days_back", days_back.Value);
            }

            filters.Number("limit", limit);
            return Task.FromResult(ResultEnvelope.Success(rows, filters.Build()));
        });
    }
}