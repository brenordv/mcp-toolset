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
}