using RaccoonNinja.McpToolset.Files.Security;
using RaccoonNinja.McpToolset.Files.Selection;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Configuration;

/// <summary>
/// One call's resolved scope: the effective <see cref="RootConfinement"/> (the base root, or a per-call
/// <c>cwd</c> beneath it) that bounds both selection and every write, the confined
/// <see cref="FileSelection"/> over it, and the base-relative <see cref="ScopeKey"/> echoed safely in
/// <c>filters_applied</c>. Input paths are relative to this scope's effective root; the writer confines
/// each candidate against <see cref="Effective"/> (the per-call write firewall) and reports/journals
/// base-relative paths.
/// </summary>
/// <param name="Effective">The effective confiner for this call (the write firewall).</param>
/// <param name="Selection">The file-selection service over the effective root.</param>
/// <param name="ScopeKey">The base-relative path of the effective root (<c>.</c> for the whole base).</param>
public sealed record EditScope(RootConfinement Effective, FileSelection Selection, string ScopeKey);