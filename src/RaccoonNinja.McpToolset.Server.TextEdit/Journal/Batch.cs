namespace RaccoonNinja.McpToolset.Server.TextEdit.Journal;

/// <summary>
/// One journaled mutation batch: a monotonic id, when it ran, which tool produced it, the root it edited,
/// and a machine-privacy-safe summary of the arguments. The summary never carries a verbatim pattern or
/// replacement body (the journal is plaintext and more persistent than the logs), only shapes and hashes.
/// </summary>
public sealed record Batch
{
    /// <summary>The monotonic batch id (SQLite <c>INTEGER PRIMARY KEY</c>).</summary>
    public long BatchId { get; init; }

    /// <summary>The ISO-8601 UTC timestamp the batch was recorded.</summary>
    public string CreatedUtc { get; init; }

    /// <summary>The tool that produced the batch (<c>replace_text</c> or <c>normalize_files</c>).</summary>
    public string Tool { get; init; }

    /// <summary>The agent-facing name of the root the batch edited.</summary>
    public string RootName { get; init; }

    /// <summary>A machine-privacy-safe summary of the call arguments (shapes and hashes, never verbatim strings).</summary>
    public string ArgsSummary { get; init; }
}