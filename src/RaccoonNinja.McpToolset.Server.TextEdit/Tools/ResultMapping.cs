using RaccoonNinja.McpToolset.Server.TextEdit.Content;
using RaccoonNinja.McpToolset.Server.TextEdit.Models;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Tools;

/// <summary>Maps the write-layer outcome records into the snake_case wire DTOs the tools return.</summary>
internal static class ResultMapping
{
    private const string OutcomeChanged = "changed";
    private const string OutcomeRefused = "refused";
    private const string OutcomeUnchanged = "unchanged";

    /// <summary>Map a batch outcome to the mutation result DTO.</summary>
    /// <param name="outcome">The write-layer batch outcome.</param>
    /// <param name="dryRun">Whether the call was a dry run.</param>
    /// <returns>The mutation result.</returns>
    public static MutationResult ToMutationResult(BatchOutcome outcome, bool dryRun)
        => new()
        {
            BatchId = outcome.BatchId,
            DryRun = dryRun,
            Attempted = outcome.Attempted,
            Changed = outcome.Changed,
            Refused = outcome.Refused,
            Files = outcome.Files.Select(ToFileChange).ToList(),
        };

    /// <summary>Map an undo outcome to the undo result DTO.</summary>
    /// <param name="outcome">The write-layer undo outcome.</param>
    /// <returns>The undo result.</returns>
    public static UndoResult ToUndoResult(UndoOutcome outcome)
        => new()
        {
            BatchId = outcome.BatchId,
            Restored = outcome.Restored,
            Recreated = outcome.Recreated,
            Skipped = outcome.Skipped.Select(skip => new SkippedFile { Path = skip.Path, Reason = skip.Reason }).ToList(),
        };

    private static FileChange ToFileChange(FileOutcome outcome)
        => new()
        {
            Path = outcome.Path,
            Outcome = outcome.Changed ? OutcomeChanged : outcome.Reason is not null ? OutcomeRefused : OutcomeUnchanged,
            RefusalReason = outcome.Reason,
            Diff = outcome.Diff,
        };
}