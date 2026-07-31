using System.ComponentModel;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.TextEdit.Content;
using RaccoonNinja.McpToolset.Server.TextEdit.Envelope;
using RaccoonNinja.McpToolset.Server.TextEdit.Errors;
using RaccoonNinja.McpToolset.Server.TextEdit.Journal;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Tools;

/// <summary>The <c>undo_batch</c> and <c>undo_last_batch</c> tools: revert a journaled batch, re-gating every restore.</summary>
[McpServerToolType]
public sealed class UndoBatchTool(ToolCommon common, JournalStore journal, Undoer undoer)
{
    /// <summary>Undo a specific batch by id.</summary>
    /// <param name="batch_id">The batch id to undo.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A single-item envelope carrying the undo result.</returns>
    [McpServerTool(Name = "undo_batch", Destructive = false, Idempotent = true)]
    [Description(
        "Undo a specific batch by id: restore each file whose content still equals what the batch wrote, "
        + "recreate a since-deleted file, and skip (naming) any file changed since, now out of the root, or "
        + "now denylisted. A file that no longer matches is never clobbered.")]
    public Task<ResultEnvelope> InvokeAsync(
        [Description("The batch id to undo (from list_recent_batches).")] long batch_id,
        CancellationToken cancellationToken = default)
    {
        var ctx = common.MakeContext("undo_batch");
        return common.WrapAsync(ctx, () => Task.FromResult(Undo(ctx, batch_id)));
    }

    /// <summary>Undo the most recent batch.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A single-item envelope carrying the undo result.</returns>
    [McpServerTool(Name = "undo_last_batch", Destructive = false, Idempotent = true)]
    [Description("Undo the most recent batch. Equivalent to undo_batch on the newest batch id.")]
    public Task<ResultEnvelope> InvokeLastAsync(CancellationToken cancellationToken = default)
    {
        var ctx = common.MakeContext("undo_last_batch");
        return common.WrapAsync(ctx, () =>
        {
            var latest = journal.LatestBatchId()
                ?? throw new TextEditException(ErrorCodes.BatchNotFound, "there are no batches to undo");
            return Task.FromResult(Undo(ctx, latest));
        });
    }

    private ResultEnvelope Undo(CallContext ctx, long batchId)
    {
        if (journal.GetBatch(batchId) is null)
        {
            throw TextEditException.BatchNotFound(batchId);
        }

        var outcome = undoer.Undo(batchId);
        common.Restored(ctx, batchId, outcome.Restored.Count + outcome.Recreated.Count);
        return ToolCommon.SingleSuccess(ResultMapping.ToUndoResult(outcome));
    }
}