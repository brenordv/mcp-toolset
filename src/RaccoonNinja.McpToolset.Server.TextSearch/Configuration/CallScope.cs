using RaccoonNinja.McpToolset.Files.Selection;
using RaccoonNinja.McpToolset.Server.TextSearch.Content;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Configuration;

/// <summary>
/// One call's resolved scope: the confined <see cref="FileSelection"/> and <see cref="GatedFileReader"/>
/// over the effective root (the base root, or a per-call <c>cwd</c> beneath it), plus the base-relative
/// <see cref="ScopeKey"/> that identifies the scope for pagination and the safe argument echo. Paths in
/// and out of the tools are relative to this scope's effective root.
/// </summary>
/// <param name="Selection">The file-selection service over the effective root.</param>
/// <param name="Reader">The read gate over the effective root.</param>
/// <param name="ScopeKey">The base-relative path of the effective root (<c>.</c> for the whole base).</param>
public sealed record CallScope(FileSelection Selection, GatedFileReader Reader, string ScopeKey);