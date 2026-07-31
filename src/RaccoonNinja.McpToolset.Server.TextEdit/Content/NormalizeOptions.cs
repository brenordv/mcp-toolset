namespace RaccoonNinja.McpToolset.Server.TextEdit.Content;

/// <summary>
/// The set of normalizations <c>normalize_files</c> applies. Every option defaults to leaving the file as
/// it is, so a caller opts in to each change; a file the options do not touch is a no-op that writes nothing.
/// </summary>
public sealed record NormalizeOptions
{
    /// <summary>Whether to strip trailing spaces and tabs from each line (before its terminator).</summary>
    public bool TrimTrailingWhitespace { get; init; }

    /// <summary>How to treat line terminators.</summary>
    public LineEndingMode LineEndings { get; init; } = LineEndingMode.Preserve;

    /// <summary>How to treat the file's final newline.</summary>
    public FinalNewlineMode FinalNewline { get; init; } = FinalNewlineMode.Preserve;

    /// <summary>How to treat a byte-order mark.</summary>
    public BomMode Bom { get; init; } = BomMode.Preserve;

    /// <summary>Whether any option asks for a change (a fully-default set is a guaranteed no-op).</summary>
    public bool IsNoOp
        => !TrimTrailingWhitespace
           && LineEndings == LineEndingMode.Preserve
           && FinalNewline == FinalNewlineMode.Preserve
           && Bom == BomMode.Preserve;
}