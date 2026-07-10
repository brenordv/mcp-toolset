using System.ComponentModel;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.FileVault.Domain;
using RaccoonNinja.McpToolset.Server.FileVault.Models;
using RaccoonNinja.McpToolset.Server.FileVault.Services;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tools;

/// <summary>The <c>vault_restore</c> tool.</summary>
[McpServerToolType]
public sealed class VaultRestoreTool(ToolCommon common, VaultService service, ProjectResolver resolver)
{
    [McpServerTool(Name = "vault_restore", UseStructuredContent = true)]
    [Description("Restore a previously archived file.")]
    public StatusResult Invoke(
        [Description("The file name to restore.")]
        string name,
        [Description("The project namespace. If omitted, it is resolved from the environment / working directory.")]
        string project = null)
        => common.Run("vault_restore", info =>
        {
            var resolvedProject = resolver.Resolve(project);
            info.Project = resolvedProject.Value;
            var fileName = FileName.Parse(name);
            info.Name = fileName.Value;

            service.Restore(resolvedProject, fileName);
            return new StatusResult { Ok = true, Message = "restored" };
        });
}