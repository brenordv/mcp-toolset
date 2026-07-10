using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.FileVault.Domain;
using RaccoonNinja.McpToolset.Server.FileVault.Models;
using RaccoonNinja.McpToolset.Server.FileVault.Services;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tools;

/// <summary>The <c>vault_edit_section</c> tool: markdown section replacement.</summary>
[McpServerToolType]
public sealed class VaultEditSectionTool(ToolCommon common, VaultService service, ProjectResolver resolver)
{
    [McpServerTool(Name = "vault_edit_section", UseStructuredContent = true)]
    [Description("Replace the body of a markdown section by heading, leaving the rest of the document byte-for-byte intact. Markdown files only.")]
    public SaveResult Invoke(
        [Description("The file name to edit.")]
        string name,
        [Description("The heading text whose body should be replaced (without leading `#`).")]
        string heading,
        [Description("The new body for that section.")]
        string content,
        [Description("The version this edit is derived from (for conflict detection).")]
        int base_version,
        [Description("The project namespace. If omitted, it is resolved from the environment / working directory.")]
        string project = null)
        => common.Run("vault_edit_section", info =>
        {
            var resolvedProject = resolver.Resolve(project);
            info.Project = resolvedProject.Value;
            var fileName = FileName.Parse(name);
            info.Name = fileName.Value;
            info.ContentSizeBytes = content is null ? 0 : Encoding.UTF8.GetByteCount(content);

            var committed = service.EditSection(resolvedProject, fileName, heading, content, base_version);
            info.CommittedChars = committed.ContentChars;
            return new SaveResult { Version = committed.Version, ContentHash = committed.Hash.Hex, SplitHint = committed.SplitHint };
        });
}