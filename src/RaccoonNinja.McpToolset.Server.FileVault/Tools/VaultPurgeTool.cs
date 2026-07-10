using System.ComponentModel;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.FileVault.Domain;
using RaccoonNinja.McpToolset.Server.FileVault.Models;
using RaccoonNinja.McpToolset.Server.FileVault.Services;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tools;

/// <summary>The <c>vault_purge</c> tool: permanent removal, confirmation-gated.</summary>
[McpServerToolType]
public sealed class VaultPurgeTool(ToolCommon common, VaultService service, ProjectResolver resolver)
{
    [McpServerTool(Name = "vault_purge", UseStructuredContent = true)]
    [Description("Permanently delete a file and all its versions. Irreversible; requires `confirm: true`.")]
    public StatusResult Invoke(
        [Description("The file name to purge.")]
        string name,
        [Description("The project namespace. If omitted, it is resolved from the environment / working directory.")]
        string project = null,
        [Description("Must be `true` to actually delete. Any other value is rejected before deletion.")]
        bool confirm = false)
        => common.Run("vault_purge", info =>
        {
            var resolvedProject = resolver.Resolve(project);
            info.Project = resolvedProject.Value;
            var fileName = FileName.Parse(name);
            info.Name = fileName.Value;

            service.Purge(resolvedProject, fileName, confirm);
            return new StatusResult { Ok = true, Message = "purged" };
        });
}