namespace RaccoonNinja.McpToolset.Server.TextEdit.Content;

/// <summary>
/// The result of applying a mutation batch: the per-file outcomes, the journal batch id when at least one
/// file was actually written (a batch that changed nothing writes no journal row, so the id is <c>null</c>),
/// and the walk metadata. The counts derive from the file outcomes so they can never disagree with them.
/// </summary>
public sealed record BatchOutcome
{
    /// <summary>The committed journal batch id, or <c>null</c> for a dry run or a batch that changed nothing.</summary>
    public long? BatchId { get; init; }

    /// <summary>The per-file outcomes, in selection order.</summary>
    public IReadOnlyList<FileOutcome> Files { get; init; } = [];

    /// <summary>How many symlinked entries the selector walk skipped.</summary>
    public int SkippedSymlinks { get; init; }

    /// <summary>Whether the selection hit its file ceiling.</summary>
    public bool Truncated { get; init; }

    /// <summary>How many files the batch attempted.</summary>
    public int Attempted => Files.Count;

    /// <summary>How many files the batch changed.</summary>
    public int Changed => Files.Count(file => file.Changed);

    /// <summary>How many files the batch refused (an unchanged file is neither changed nor refused).</summary>
    public int Refused => Files.Count(file => file.Reason is not null);
}