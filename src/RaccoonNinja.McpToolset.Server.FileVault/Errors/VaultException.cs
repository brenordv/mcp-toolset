using System.Globalization;

namespace RaccoonNinja.McpToolset.Server.FileVault.Errors;

/// <summary>
/// The single domain exception. Carries a discriminable <see cref="Code"/> plus the typed
/// payload fields the error body exposes to the client (see <c>ErrorMapping</c>).
/// </summary>
/// <remarks>
/// The <see cref="Exception.Message"/> mirrors the Rust server's <c>Display</c> wording and is
/// client-visible by design; it may embed user-supplied text (heading, key path, name). For that
/// reason a <see cref="VaultException"/> must never be logged as an exception object — the
/// logging layer records only <see cref="Code"/>.
/// </remarks>
public sealed class VaultException : Exception
{
    private VaultException(VaultErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>The discriminable domain error code.</summary>
    public VaultErrorCode Code { get; }

    /// <summary>Project payload field (<c>not_found</c>, <c>archived</c>, <c>parent_not_found</c>).</summary>
    public string Project { get; private init; }

    /// <summary>Name payload field (<c>not_found</c>, <c>archived</c>).</summary>
    public string Name { get; private init; }

    /// <summary>Parent payload field (<c>parent_not_found</c>).</summary>
    public string Parent { get; private init; }

    /// <summary>The file's current version (<c>conflict</c>).</summary>
    public int? CurrentVersion { get; private init; }

    /// <summary>The stale base version the caller wrote from (<c>conflict</c> with hint).</summary>
    public int? BaseVersion { get; private init; }

    /// <summary>The base-to-current line diff hint (<c>conflict</c> with hint).</summary>
    public string Diff { get; private init; }

    /// <summary>The project strings that were tried and rejected (<c>ambiguous_project</c>).</summary>
    public IReadOnlyList<string> Tried { get; private init; }

    /// <summary>No active or archived file exists for the pair.</summary>
    /// <param name="project">The project namespace.</param>
    /// <param name="name">The file name (or the Rust-parity <c>"{name} (version {N})"</c> form).</param>
    /// <returns>The composed exception.</returns>
    public static VaultException NotFound(string project, string name)
        => new(VaultErrorCode.NotFound, $"no file named '{name}' in project '{project}'")
        {
            Project = project,
            Name = name,
        };

    /// <summary>A stale write was rejected; no diff hint is available.</summary>
    /// <param name="currentVersion">The version currently stored.</param>
    /// <returns>The composed exception.</returns>
    public static VaultException Conflict(int currentVersion)
        => new(VaultErrorCode.Conflict, ConflictMessage(currentVersion))
        {
            CurrentVersion = currentVersion,
        };

    /// <summary>A stale write was rejected, with a base-to-current diff hint attached.</summary>
    /// <param name="currentVersion">The version currently stored.</param>
    /// <param name="baseVersion">The version the caller derived its write from.</param>
    /// <param name="diff">The line diff from the base content to the current content.</param>
    /// <returns>The composed exception.</returns>
    public static VaultException ConflictWithHint(int currentVersion, int baseVersion, string diff)
        => new(VaultErrorCode.Conflict, ConflictMessage(currentVersion))
        {
            CurrentVersion = currentVersion,
            BaseVersion = baseVersion,
            Diff = diff,
        };

    /// <summary>The file is archived and must be restored before being written.</summary>
    /// <param name="project">The project namespace.</param>
    /// <param name="name">The file name.</param>
    /// <returns>The composed exception.</returns>
    public static VaultException Archived(string project, string name)
        => new(VaultErrorCode.Archived, $"file '{name}' in project '{project}' is archived; restore it before editing")
        {
            Project = project,
            Name = name,
        };

    /// <summary>A project or file name failed validation.</summary>
    /// <param name="reason">The human-readable rejection reason.</param>
    /// <returns>The composed exception.</returns>
    public static VaultException InvalidName(string reason)
        => new(VaultErrorCode.InvalidName, $"invalid name: {reason}");

    /// <summary>The operation does not apply to the file's format.</summary>
    /// <param name="reason">The human-readable rejection reason.</param>
    /// <returns>The composed exception.</returns>
    public static VaultException InvalidFormat(string reason)
        => new(VaultErrorCode.InvalidFormat, $"invalid format: {reason}");

    /// <summary>Content exceeded the configured per-call byte limit.</summary>
    /// <param name="limit">The configured limit, in bytes.</param>
    /// <param name="actual">The offending payload size, in bytes.</param>
    /// <returns>The composed exception.</returns>
    public static VaultException TooLarge(long limit, long actual)
        => new(
            VaultErrorCode.TooLarge,
            string.Create(
                CultureInfo.InvariantCulture,
                $"content is {actual} bytes, which exceeds the {limit}-byte limit"));

    /// <summary>A metadata cap was exceeded (counts/lengths rather than content bytes).</summary>
    /// <param name="message">The specific limit that was exceeded, already worded for the client.</param>
    /// <returns>The composed exception.</returns>
    public static VaultException TooLarge(string message)
        => new(VaultErrorCode.TooLarge, message);

    /// <summary>No project could be resolved; the caller must pass one explicitly.</summary>
    /// <param name="tried">The candidate strings that were tried and rejected.</param>
    /// <returns>The composed exception.</returns>
    public static VaultException AmbiguousProject(IReadOnlyList<string> tried)
        => new(
            VaultErrorCode.AmbiguousProject,
            $"could not resolve a project (tried: {string.Join(", ", tried ?? [])}); pass `project` explicitly")
        {
            Tried = tried ?? [],
        };

    /// <summary>edit_section found no markdown heading matching the target.</summary>
    /// <param name="heading">The heading text that was searched for.</param>
    /// <returns>The composed exception.</returns>
    public static VaultException HeadingNotFound(string heading)
        => new(VaultErrorCode.HeadingNotFound, $"no markdown heading matching '{heading}'");

    /// <summary>edit_section matched more than one identical heading.</summary>
    /// <param name="heading">The ambiguous heading text.</param>
    /// <returns>The composed exception.</returns>
    public static VaultException AmbiguousHeading(string heading)
        => new(
            VaultErrorCode.AmbiguousHeading,
            $"heading '{heading}' is ambiguous (appears more than once); make it unique first");

    /// <summary>edit_key could not resolve the dotted key path.</summary>
    /// <param name="keyPath">The key path that failed to resolve.</param>
    /// <returns>The composed exception.</returns>
    public static VaultException KeyPathNotFound(string keyPath)
        => new(VaultErrorCode.KeyPathNotFound, $"key path '{keyPath}' was not found in the document");

    /// <summary>A destructive operation was attempted without <c>confirm: true</c>.</summary>
    /// <returns>The composed exception.</returns>
    public static VaultException ConfirmationRequired()
        => new(VaultErrorCode.ConfirmationRequired, "this operation is permanent and requires `confirm: true`");

    /// <summary>A parent link named a file that does not exist in the project.</summary>
    /// <param name="project">The project namespace.</param>
    /// <param name="parent">The missing parent file name.</param>
    /// <returns>The composed exception.</returns>
    public static VaultException ParentNotFound(string project, string parent)
        => new(VaultErrorCode.ParentNotFound, $"no parent file named '{parent}' in project '{project}'")
        {
            Project = project,
            Parent = parent,
        };

    /// <summary>A parent link would be self-referential, cross-project, or cycle-forming.</summary>
    /// <param name="reason">The human-readable rejection reason.</param>
    /// <returns>The composed exception.</returns>
    public static VaultException InvalidParent(string reason)
        => new(VaultErrorCode.InvalidParent, $"invalid parent link: {reason}");

    /// <summary>set_meta was called with no field to change.</summary>
    /// <returns>The composed exception.</returns>
    public static VaultException NothingToUpdate()
        => new(
            VaultErrorCode.NothingToUpdate,
            "set_meta requires at least one of `summary`, `tags`, or a parent change");

    private static string ConflictMessage(int currentVersion)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"version conflict: the file has changed (current version is {currentVersion}); "
            + $"re-read, re-apply your edit, and retry with base_version = {currentVersion}");
}