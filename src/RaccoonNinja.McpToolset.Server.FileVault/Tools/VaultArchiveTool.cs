using System.ComponentModel;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.FileVault.Domain;
using RaccoonNinja.McpToolset.Server.FileVault.Models;
using RaccoonNinja.McpToolset.Server.FileVault.Services;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tools;

/// <summary>The <c>vault_archive</c> tool: soft delete.</summary>
[McpServerToolType]
public sealed class VaultArchiveTool(ToolCommon common, VaultService service, ProjectResolver resolver)
{
    [McpServerTool(Name = "vault_archive", UseStructuredContent = true)]
    [Description("Archive a file (soft delete). It disappears from listings but is fully recoverable.")]
    public StatusResult Invoke(
        [Description("The file name to archive.")]
        string name,
        [Description("The project namespace. If omitted, it is resolved from the environment / working directory.")]
        string project = null)
        => common.Run("vault_archive", info =>
        {
            var resolvedProject = resolver.Resolve(project);
            info.Project = resolvedProject.Value;
            var fileName = FileName.Parse(name);
            info.Name = fileName.Value;

            service.Archive(resolvedProject, fileName);
            return new StatusResult { Ok = true, Message = "archived" };
        });
}