namespace RaccoonNinja.McpToolset.Server.TextEdit.Content;

/// <summary>How <c>normalize_files</c> should treat the file's final newline.</summary>
public enum FinalNewlineMode
{
    /// <summary>Leave the final newline as it is.</summary>
    Preserve = 1,

    /// <summary>Ensure the file ends with exactly one terminator.</summary>
    Ensure = 2,

    /// <summary>Remove any trailing terminators so the file does not end with a newline.</summary>
    Trim = 3,
}