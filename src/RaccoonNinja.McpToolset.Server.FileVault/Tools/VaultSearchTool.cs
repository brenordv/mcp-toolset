using System.ComponentModel;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.FileVault.Models;
using RaccoonNinja.McpToolset.Server.FileVault.Services;
using RaccoonNinja.McpToolset.Server.FileVault.Storage;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tools;

/// <summary>
/// The <c>vault_search</c> tool: full-text search INSIDE note bodies, returning capped per-note
/// snippets rather than whole bodies. Metadata (name/summary/tags) is <c>vault_list</c>'s domain.
/// An omitted <c>project</c> means "across ALL projects" with no inference, mirroring
/// <c>vault_list</c>.
/// </summary>
[McpServerToolType]
public sealed class VaultSearchTool(ToolCommon common, VaultService service, ProjectResolver resolver)
{
    [McpServerTool(Name = "vault_search", UseStructuredContent = true)]
    [Description(
        "Search INSIDE the bodies of active notes for case-insensitive substring terms, returning per-note "
        + "snippets with match context, never whole bodies. Every term must match; when that matches nothing "
        + "a ranked any-term fallback runs automatically, and `query_mode` reports which pass ran. Only the "
        + "first 16 terms are used. Results are capped (default 20, max 100). This searches note bodies only; "
        + "names, summaries, and tags are searched by `vault_list`. Use `vault_get` to read a whole note.")]
    public VaultSearchResult Invoke(
        [Description(
            "The search text. Terms are case-insensitive substrings matched against note bodies; at least one "
            + "non-whitespace term is required, and only the first 16 are used.")]
        string query,
        [Description("The project namespace to search. If omitted, all projects are searched (no inference).")]
        string project = null,
        [Description("Maximum notes to return (default 20, max 100).")]
        int? limit = null)
        => common.Run("vault_search", info =>
        {
            var resolvedProject = project is null ? null : resolver.Resolve(project);
            info.Project = resolvedProject?.Value;
            common.LogQueryShape("vault_search", query);

            var outcome = service.Search(resolvedProject, query, limit);
            var queryMode = outcome.Mode.ToWireString();
            info.QueryMode = queryMode;
            info.NotesScanned = outcome.NotesScanned;
            info.NotesMatched = outcome.Count;
            info.BytesScanned = outcome.BytesScanned;
            info.SkippedUnreadable = outcome.Skipped.Count;

            common.ReportSnapshotSkips("vault_search", outcome.Skipped);
            if (outcome.Mode == ListMode.AnyTermFallback)
            {
                common.LogQueryFallback("vault_search", query, outcome.Count);
            }

            return new VaultSearchResult
            {
                Items = [.. outcome.Items.Select(ToItem)],
                Count = outcome.Count,
                Truncated = outcome.Truncated,
                QueryMode = queryMode,
                Skipped = outcome.Skipped.Count,
            };
        });

    private static VaultSearchItem ToItem(ContentMatch match)
        => new()
        {
            Project = match.Candidate.Project,
            Name = match.Candidate.Name,
            Summary = match.Candidate.Summary,
            CurrentVersion = match.Candidate.CurrentVersion,
            UpdatedAt = match.Candidate.UpdatedAt,
            MatchedTerms = match.MatchedTerms,
            Snippets = [.. match.Snippets.Select(snippet => new VaultSearchSnippet { Term = snippet.Term, Text = snippet.Text })],
        };
}