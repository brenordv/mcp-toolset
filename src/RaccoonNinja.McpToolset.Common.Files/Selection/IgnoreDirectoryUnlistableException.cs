namespace RaccoonNinja.McpToolset.Common.Files.Selection;

/// <summary>
/// Thrown when a directory whose ignore files must be read exists but cannot be listed. The message names the
/// directory by its root-relative path and the type of the filesystem exception that stopped the listing,
/// never by its absolute path. An unlistable directory is never treated as having no ignore files: on the
/// explicit-path route the server can open a file by name inside it, so the rules it may hold must be known
/// before any read.
/// </summary>
public sealed class IgnoreDirectoryUnlistableException : Exception
{
    /// <summary>Create the exception for the directory at <paramref name="relativeDirectory"/>.</summary>
    /// <param name="relativeDirectory">The root-relative path of the directory (<c>.</c> for the root); never an absolute path.</param>
    /// <param name="causeType">The type name of the filesystem exception that stopped the listing.</param>
    public IgnoreDirectoryUnlistableException(string relativeDirectory, string causeType)
        : base($"could not list directory '{relativeDirectory}' for ignore files ({causeType})")
    {
    }
}