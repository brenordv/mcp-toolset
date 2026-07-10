namespace RaccoonNinja.McpToolset.Server.FileVault.Errors;

/// <summary>
/// The discriminable domain error codes surfaced to the MCP boundary. The wire strings
/// (see <see cref="VaultErrorCodeExtensions.ToWireCode"/>) are a stable contract the
/// assistant branches on; they must not change casually.
/// </summary>
public enum VaultErrorCode
{
    /// <summary>No active or archived file exists for the (project, name) pair.</summary>
    NotFound,

    /// <summary>The write was derived from a stale version.</summary>
    Conflict,

    /// <summary>The file exists but is archived; it must be restored before being written.</summary>
    Archived,

    /// <summary>A project or file name violated the allowed charset / traversal rules.</summary>
    InvalidName,

    /// <summary>The requested operation does not apply to the file's format.</summary>
    InvalidFormat,

    /// <summary>Content exceeded the configured per-call byte limit.</summary>
    TooLarge,

    /// <summary>The project could not be resolved and must be supplied explicitly.</summary>
    AmbiguousProject,

    /// <summary>edit_section could not find the requested heading.</summary>
    HeadingNotFound,

    /// <summary>edit_section found more than one identical heading.</summary>
    AmbiguousHeading,

    /// <summary>edit_key could not resolve the key path in the document.</summary>
    KeyPathNotFound,

    /// <summary>A destructive operation (purge) was attempted without confirm: true.</summary>
    ConfirmationRequired,

    /// <summary>A parent link named a file that does not exist in the project.</summary>
    ParentNotFound,

    /// <summary>A parent link would be invalid: self-referential, cross-project, or cycle-forming.</summary>
    InvalidParent,

    /// <summary>A metadata-only update (set_meta) was called with no field to change.</summary>
    NothingToUpdate,
}