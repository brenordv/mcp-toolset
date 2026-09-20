namespace RaccoonNinja.McpToolset.Server.FileVault.Tools;

/// <summary>
/// Mutable per-call identifiers a tool body fills in as it resolves them (project, name, write
/// size), so the shared wrapper can log them even when the body fails midway. All fields are
/// loggable identifiers per the <c>LogFields</c> classification rule.
/// </summary>
public sealed record CallInfo
{
    /// <summary>The resolved project namespace, once known.</summary>
    public string Project { get; set; }

    /// <summary>The validated file name, once known.</summary>
    public string Name { get; set; }

    /// <summary>The content payload size in bytes, for write tools.</summary>
    public long? ContentSizeBytes { get; set; }

    /// <summary>
    /// The committed full-content length in UTF-16 code units, for write tools. Differs from
    /// <see cref="ContentSizeBytes"/> on append/edit, where the payload is only a delta.
    /// </summary>
    public int? CommittedChars { get; set; }

    /// <summary>The matching mode (wire string) for a query tool, once known.</summary>
    public string QueryMode { get; set; }

    /// <summary>The item count on a returned <c>vault_list</c> page.</summary>
    public int? PageItems { get; set; }

    /// <summary>Whether more results existed beyond the returned page.</summary>
    public bool? Truncated { get; set; }

    /// <summary>The opaque hash of a cursor argument, when one was supplied.</summary>
    public string CursorHash { get; set; }

    /// <summary>How many notes <c>vault_search</c> enumerated.</summary>
    public int? NotesScanned { get; set; }

    /// <summary>How many notes matched, for <c>vault_search</c>.</summary>
    public int? NotesMatched { get; set; }

    /// <summary>Total bytes read across scanned snapshots, for <c>vault_search</c>.</summary>
    public long? BytesScanned { get; set; }

    /// <summary>How many notes were skipped because their snapshot could not be read.</summary>
    public int? SkippedUnreadable { get; set; }
}