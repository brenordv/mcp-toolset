namespace RaccoonNinja.McpToolset.Common.Files.Selection;

/// <summary>
/// Thrown when an ignore file exists but cannot be read. The message names the file by its root-relative
/// path and the type of the filesystem exception that stopped the read, never by its absolute path: a
/// filesystem exception's own message usually carries the absolute path, and server logs must not hold
/// one. The read is never skipped instead, because treating an unreadable ignore file as empty would
/// surface the paths it hides.
/// </summary>
public sealed class IgnoreFileReadException : Exception
{
    /// <summary>Create the exception for the ignore file at <paramref name="relativePath"/>.</summary>
    /// <param name="relativePath">The root-relative path of the ignore file; never an absolute path.</param>
    /// <param name="causeType">The type name of the filesystem exception that stopped the read.</param>
    public IgnoreFileReadException(string relativePath, string causeType)
        : base($"could not read ignore file '{relativePath}' ({causeType})")
    {
    }
}