namespace RaccoonNinja.McpToolset.Server.TextSearch.Content;

/// <summary>The outcome category of a gated content read.</summary>
public enum ReadStatus
{
    /// <summary>The file was read; its bytes are available.</summary>
    Ok = 1,

    /// <summary>The path resolved outside the confinement root.</summary>
    OutOfRoot = 2,

    /// <summary>The path is denylisted; it is never read.</summary>
    Denied = 3,

    /// <summary>The path does not exist.</summary>
    NotFound = 4,

    /// <summary>The path resolved to a directory, not a file.</summary>
    IsDirectory = 5,

    /// <summary>The file exceeded the configured size cap.</summary>
    TooLarge = 6,

    /// <summary>The file could not be opened or read (locked, permission, mid-read I/O error).</summary>
    IoError = 7,
}