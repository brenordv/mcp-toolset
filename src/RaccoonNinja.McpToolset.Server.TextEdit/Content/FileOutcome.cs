namespace RaccoonNinja.McpToolset.Server.TextEdit.Content;

/// <summary>
/// The gate's verdict on one file: changed, refused with a reason, or attempted-but-unchanged (neither
/// changed nor refused). The optional diff is populated only for a <c>dry_run</c> preview.
/// </summary>
public sealed record FileOutcome
{
    /// <summary>The root-relative path of the file.</summary>
    public string Path { get; init; }

    /// <summary>Whether the file was (or, in a dry run, would be) rewritten.</summary>
    public bool Changed { get; init; }

    /// <summary>The refusal reason (see <see cref="RefusalReason"/>), or <c>null</c> when the file was not refused.</summary>
    public string Reason { get; init; }

    /// <summary>The unified diff of the change, populated only for a <c>dry_run</c>; otherwise <c>null</c>.</summary>
    public string Diff { get; init; }
}