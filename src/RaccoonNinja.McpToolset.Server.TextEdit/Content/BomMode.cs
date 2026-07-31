namespace RaccoonNinja.McpToolset.Server.TextEdit.Content;

/// <summary>How <c>normalize_files</c> should treat a byte-order mark.</summary>
public enum BomMode
{
    /// <summary>Keep the file's existing byte-order-mark state.</summary>
    Preserve = 1,

    /// <summary>Remove a leading byte-order mark if present.</summary>
    Strip = 2,
}