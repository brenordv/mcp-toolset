using System.ComponentModel;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.TextEdit.Configuration;
using RaccoonNinja.McpToolset.Server.TextEdit.Envelope;
using RaccoonNinja.McpToolset.Server.TextEdit.Journal;
using RaccoonNinja.McpToolset.Server.TextEdit.Models;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Tools;

/// <summary>The <c>list_recent_batches</c> tool: report the most recent undoable mutation batches, newest first.</summary>
[McpServerToolType]
public sealed class ListRecentBatchesTool(ToolCommon common, EditConfig config, JournalStore journal)
{
    /// <summary>List the most recent batches, each with its id, timestamp, tool, and changed-file count.</summary>
    /// <param name="limit">The maximum number of batches to return; zero uses the retention count.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An envelope carrying the batch list, newest first.</returns>
    [McpServerTool(Name = "list_recent_batches", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "List the most recent mutation batches, newest first, each with its id, timestamp, tool, and "
        + "changed-file count. Pass a batch id to undo_batch to revert it.")]
    public Task<ResultEnvelope> InvokeAsync(
        [Description("Maximum batches to return (0 uses the journal retention count).")] int limit = 0,
        CancellationToken cancellationToken = default)
    {
        var ctx = common.MakeContext("list_recent_batches");
        return common.WrapAsync(ctx, () =>
        {
            var effective = limit <= 0
                ? config.JournalRetentionBatches
                : Math.Min(limit, config.JournalRetentionBatches);

            var items = journal.ListRecent(effective)
                .Select(summary => (object)new BatchListItem
                {
                    BatchId = summary.BatchId,
                    Timestamp = summary.CreatedUtc,
                    Tool = summary.Tool,
                    Changed = summary.ChangedCount,
                })
                .ToList();

            return Task.FromResult(ToolCommon.ListSuccess(items));
        });
    }
}