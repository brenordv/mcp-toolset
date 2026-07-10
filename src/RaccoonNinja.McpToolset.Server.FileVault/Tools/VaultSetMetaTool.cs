using System.ComponentModel;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.FileVault.Domain;
using RaccoonNinja.McpToolset.Server.FileVault.Models;
using RaccoonNinja.McpToolset.Server.FileVault.Services;
using RaccoonNinja.McpToolset.Server.FileVault.Storage;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tools;

/// <summary>The <c>vault_set_meta</c> tool: a metadata-only update that never creates a new version.</summary>
[McpServerToolType]
public sealed class VaultSetMetaTool(ToolCommon common, VaultService service, ProjectResolver resolver)
{
    [McpServerTool(Name = "vault_set_meta", UseStructuredContent = true)]
    [Description("Update only a note's metadata — `summary`, `tags`, and/or `parent` — without resending or changing its content. This does **not** create a new version. Provide at least one of `summary`, `tags`, or a parent change. Set `clear_parent: true` to detach a note from its parent (top-level); otherwise `parent` links it under the named note in the same project.")]
    public SetMetaResult Invoke(
        [Description("The file whose metadata to update.")]
        string name,
        [Description("The project namespace. If omitted, it is resolved from the environment / working directory.")]
        string project = null,
        [Description("New one-line summary. Omit to leave the summary unchanged.")]
        string summary = null,
        [Description("New complete tag set (replaces the existing tags). Omit to leave tags unchanged.")]
        string[] tags = null,
        [Description("Parent file name (same project) to link this note under. Omit to leave the parent unchanged; to detach the note instead, set `clear_parent: true`.")]
        string parent = null,
        [Description("Set to `true` to detach the note from its parent (make it top-level). Takes precedence over `parent`.")]
        bool clear_parent = false,
        [Description("Optional staleness guard: if given and it does not match the current version, the update is rejected as a conflict. A metadata update never bumps the version.")]
        int? base_version = null)
        => common.Run("vault_set_meta", info =>
        {
            var resolvedProject = resolver.Resolve(project);
            info.Project = resolvedProject.Value;
            var fileName = FileName.Parse(name);
            info.Name = fileName.Value;

            // clear_parent takes precedence; otherwise a provided parent is validated and links,
            // and an omitted one leaves the existing link untouched.
            var parentUpdate = clear_parent
                ? ParentUpdate.Clear
                : parent is null
                    ? ParentUpdate.Leave
                    : ParentUpdate.Set(FileName.Parse(parent).Value);

            var currentVersion = service.SetMeta(resolvedProject, fileName, summary, tags, parentUpdate, base_version);
            return new SetMetaResult { Ok = true, CurrentVersion = currentVersion };
        });
}