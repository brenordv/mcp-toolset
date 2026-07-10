using System.ComponentModel;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.FileVault.Domain;
using RaccoonNinja.McpToolset.Server.FileVault.Models;
using RaccoonNinja.McpToolset.Server.FileVault.Services;
using RaccoonNinja.McpToolset.Server.FileVault.Storage;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tools;

/// <summary>The <c>vault_history</c> tool.</summary>
[McpServerToolType]
public sealed class VaultHistoryTool(ToolCommon common, VaultService service, ProjectResolver resolver)
{
    [McpServerTool(Name = "vault_history", UseStructuredContent = true)]
    [Description("List the full version history of a file, newest first.")]
    public HistoryResult Invoke(
        [Description("The file name whose history to list.")]
        string name,
        [Description("The project namespace. If omitted, it is resolved from the environment / working directory.")]
        string project = null)
        => common.Run("vault_history", info =>
        {
            var resolvedProject = resolver.Resolve(project);
            info.Project = resolvedProject.Value;
            var fileName = FileName.Parse(name);
            info.Name = fileName.Value;

            var rows = service.History(resolvedProject, fileName);
            return new HistoryResult
            {
                Versions = [.. rows
                    .Select(row => new HistoryItem
                    {
                        Version = row.Version,
                        Op = row.Op.ToDbString(),
                        Summary = row.Summary,
                        ByteSize = row.ByteSize,
                        ContentHash = row.Hash.Hex,
                        CreatedAt = row.CreatedAt,
                    })],
            };
        });
}