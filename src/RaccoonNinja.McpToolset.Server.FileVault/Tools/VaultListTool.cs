using System.ComponentModel;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.FileVault.Logging;
using RaccoonNinja.McpToolset.Server.FileVault.Models;
using RaccoonNinja.McpToolset.Server.FileVault.Services;
using RaccoonNinja.McpToolset.Server.FileVault.Storage;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tools;

/// <summary>
/// The <c>vault_list</c> tool. An omitted <c>project</c> means "across ALL projects": no cwd/env
/// inference happens for listings (Rust parity, load-bearing for cross-project namespaces like
/// <c>lessons</c>). An explicit empty string still goes through the resolve chain.
/// </summary>
[McpServerToolType]
public sealed class VaultListTool(ToolCommon common, VaultService service, ProjectResolver resolver)
{
    [McpServerTool(Name = "vault_list", UseStructuredContent = true)]
    [Description(
        "List active files with their summaries and tags, newest first. Filter by project, required tags, "
        + "or a keyword query over name/summary/tags (whole-token full-text). Every query term must match; "
        + "when that matches nothing a ranked any-term fallback runs automatically, and `query_mode` reports "
        + "which pass ran. Only the first 16 query terms are used. Results are paginated (default 50 per "
        + "page): pass the returned `cursor` back, with the other arguments unchanged, to get the next page. "
        + "Fallback results are not paginated; refine the query instead. This searches metadata only; use "
        + "`vault_search` to search inside note bodies.")]
    public VaultListResult Invoke(
        [Description("The project namespace. If omitted, files across ALL projects are listed (no inference).")]
        string project = null,
        [Description("Only return files carrying all of these tags.")]
        string[] tags = null,
        [Description(
            "Keyword query over name/summary/tags (whole-token full-text). Every term must match; if none "
            + "do, a ranked any-term fallback runs (see `query_mode`). Only the first 16 terms are used.")]
        string query = null,
        [Description("Maximum items per page (default 50, max 500).")]
        int? limit = null,
        [Description(
            "Opaque pagination cursor from a previous page's `cursor`. Pass it back with the other arguments "
            + "unchanged to continue. Not issued for fallback results.")]
        string cursor = null)
        => common.Run("vault_list", info =>
        {
            var resolvedProject = project is null ? null : resolver.Resolve(project);
            info.Project = resolvedProject?.Value;
            common.LogQueryShape("vault_list", query);
            if (cursor is not null)
            {
                info.CursorHash = LogScrubbing.HashedParameter(cursor);
            }

            var page = service.ListPage(resolvedProject, tags, query, limit, cursor);
            var queryMode = page.QueryMode is { } mode ? mode.ToWireString() : null;
            info.QueryMode = queryMode;
            info.PageItems = page.Count;
            info.Truncated = page.Truncated;
            if (page.QueryMode == ListMode.AnyTermFallback)
            {
                common.LogQueryFallback("vault_list", query, page.Count);
            }

            return new VaultListResult
            {
                Items = [.. page.Items
                    .Select(row => new VaultListItem
                    {
                        Name = row.Name,
                        Project = row.Project,
                        Summary = row.Summary,
                        Tags = row.Tags,
                        CurrentVersion = row.CurrentVersion,
                        UpdatedAt = row.UpdatedAt,
                        Parent = row.Parent,
                    })],
                Count = page.Count,
                Truncated = page.Truncated,
                Cursor = page.Cursor,
                QueryMode = queryMode,
            };
        });
}