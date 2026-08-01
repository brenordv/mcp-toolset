namespace RaccoonNinja.McpToolset.Server.TextEdit.Content;

/// <summary>One journal row an undo declined to restore, named with the reason (see <see cref="UndoSkipReason"/>).</summary>
/// <param name="Path">The root-relative path that was skipped.</param>
/// <param name="Reason">Why it was skipped.</param>
public sealed record SkippedUndo(string Path, string Reason);