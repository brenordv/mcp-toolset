using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.FileVault.Logging;
using RaccoonNinja.McpToolset.Server.FileVault.Models;
using RaccoonNinja.McpToolset.Server.FileVault.Services;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tools;

/// <summary>
/// The <c>vault_list</c> tool. An omitted <c>project</c> means "across ALL projects": no cwd/env
/// inference happens for listings (Rust parity, load-bearing for cross-project namespaces like
/// <c>lessons</c>). An explicit empty string still goes through the resolve chain.
/// </summary>
[McpServerToolType]
public sealed class VaultListTool(ToolCommon common, VaultService service, ProjectResolver resolver, ILoggerFactory loggerFactory)
{
    [McpServerTool(Name = "vault_list", UseStructuredContent = true)]
    [Description("List active files with their summaries and tags. Optionally filter by project, required tags, or a keyword query (full-text over name/summary/tags).")]
    public VaultListResult Invoke(
        [Description("The project namespace. If omitted, it is resolved from the environment / working directory.")]
        string project = null,
        [Description("Only return files carrying all of these tags.")]
        string[] tags = null,
        [Description("Keyword query over name/summary/tags (FTS).")]
        string query = null)
        => common.Run("vault_list", info =>
        {
            var resolvedProject = project is null ? null : resolver.Resolve(project);
            info.Project = resolvedProject?.Value;
            LogQueryShape(query);

            var rows = service.List(resolvedProject, tags, query);
            return new VaultListResult
            {
                Items = [.. rows
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
            };
        });

    /// <summary>Content-free search debuggability: token count, byte length, and a hash of the query.</summary>
    private void LogQueryShape(string query)
    {
        if (query is null)
        {
            return;
        }

        var logger = loggerFactory.CreateLogger("vault_list");
        var tokens = query.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length;
        var bytes = Encoding.UTF8.GetByteCount(query);
        var hash = LogScrubbing.HashedParameter(query);

        // The allowlisted snake_case fields ride a scope (as in ToolCommon); the PascalCase
        // template placeholders only render into the message text.
        var scope = new Dictionary<string, object>
        {
            [LogFields.Event] = "list_query",
            [LogFields.QueryTokens] = tokens,
            [LogFields.QueryBytes] = bytes,
            [LogFields.QueryHash] = hash,
        };
        using (logger.BeginScope(scope))
        {
            LogQueryMessage(logger, tokens, bytes, hash, null);
        }
    }

    private static readonly Action<ILogger, int, int, string, Exception> LogQueryMessage =
        LoggerMessage.Define<int, int, string>(
            LogLevel.Debug,
            new EventId(2110, "list_query"),
            "list query shape: tokens={QueryTokens} bytes={QueryBytes} hash={QueryHash}");
}