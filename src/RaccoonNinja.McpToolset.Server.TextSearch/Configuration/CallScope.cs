using RaccoonNinja.McpToolset.Files.Selection;
using RaccoonNinja.McpToolset.Server.TextSearch.Content;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Configuration;

/// <summary>
/// One call's resolved scope: the confined <see cref="FileSelection"/> and <see cref="GatedFileReader"/>
/// over the effective root (the base root or a per-call <c>cwd</c> beneath it, or a package root or a
/// subpath beneath it), plus the display <see cref="ScopeKey"/> that identifies the scope for the safe
/// argument echo. Paths in and out of the tools are relative to this scope's effective root.
/// </summary>
/// <param name="Selection">The file-selection service over the effective root.</param>
/// <param name="Reader">The read gate over the effective root.</param>
/// <param name="Kind">Which configured root this scope resolved under (base or a package root).</param>
/// <param name="ScopeKey">
/// The display scope key: the base-relative path of the effective root (<c>.</c> for the whole base, kind
/// <see cref="ScopeKind.Base"/>), or <c>@name[/subpath]</c> for a package root (kind
/// <see cref="ScopeKind.Package"/>). It is path-free and echoed in <c>filters_applied</c>.
/// </param>
public sealed record CallScope(FileSelection Selection, GatedFileReader Reader, ScopeKind Kind, string ScopeKey)
{
    // A record separator (U+001E): a filesystem path and a root name can never contain it, so combining it
    // with the scope key yields a cursor identity that a base subtree named "@foo" cannot alias to package
    // "foo".
    private const char CursorFieldSeparator = '\u001E';

    /// <summary>
    /// The cursor-pinning identity: <see cref="Kind"/> and <see cref="ScopeKey"/> joined by a record
    /// separator. Two scopes with the same readable <see cref="ScopeKey"/> but different kinds (a base
    /// subtree <c>@foo</c> versus package root <c>foo</c>) get distinct cursor identities.
    /// </summary>
    public string CursorScope => $"{Kind}{CursorFieldSeparator}{ScopeKey}";

    /// <summary>
    /// The package root's configured name for a <see cref="ScopeKind.Package"/> scope (parsed from
    /// <see cref="ScopeKey"/> up to the first <c>/</c>), or <c>null</c> for a base scope. Used for the
    /// path-free package-root metric and log line; never carries a subpath.
    /// </summary>
    public string PackageName => Kind == ScopeKind.Package && ScopeKey.StartsWith('@')
        ? ScopeKey[1..].Split('/', 2)[0]
        : null;
}
