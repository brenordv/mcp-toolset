namespace RaccoonNinja.McpToolset.Server.FileVault.Errors;

/// <summary>Maps <see cref="VaultErrorCode"/> values to their stable wire strings.</summary>
public static class VaultErrorCodeExtensions
{
    /// <summary>
    /// The stable, machine-readable error code string carried in every error body.
    /// These strings match the Rust server byte-for-byte and are part of the contract.
    /// </summary>
    /// <param name="code">The domain error code.</param>
    /// <returns>The snake_case wire string for <paramref name="code"/>.</returns>
    public static string ToWireCode(this VaultErrorCode code)
        => code switch
        {
            VaultErrorCode.NotFound => "not_found",
            VaultErrorCode.Conflict => "conflict",
            VaultErrorCode.Archived => "archived",
            VaultErrorCode.InvalidName => "invalid_name",
            VaultErrorCode.InvalidFormat => "invalid_format",
            VaultErrorCode.TooLarge => "too_large",
            VaultErrorCode.AmbiguousProject => "ambiguous_project",
            VaultErrorCode.HeadingNotFound => "heading_not_found",
            VaultErrorCode.AmbiguousHeading => "ambiguous_heading",
            VaultErrorCode.KeyPathNotFound => "key_path_not_found",
            VaultErrorCode.ConfirmationRequired => "confirmation_required",
            VaultErrorCode.ParentNotFound => "parent_not_found",
            VaultErrorCode.InvalidParent => "invalid_parent",
            VaultErrorCode.NothingToUpdate => "nothing_to_update",
            _ => "internal",
        };
}