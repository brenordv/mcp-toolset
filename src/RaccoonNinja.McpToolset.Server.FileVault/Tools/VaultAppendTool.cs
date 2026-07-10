using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.FileVault.Domain;
using RaccoonNinja.McpToolset.Server.FileVault.Models;
using RaccoonNinja.McpToolset.Server.FileVault.Services;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tools;

/// <summary>The <c>vault_append</c> tool: a versioned write that concatenates content.</summary>
[McpServerToolType]
public sealed class VaultAppendTool(ToolCommon common, VaultService service, ProjectResolver resolver)
{
    [McpServerTool(Name = "vault_append", UseStructuredContent = true)]
    [Description("Append content to the end of a file as a new version. Distinct from save — for logs, running notes, and lists. The result carries a `hint` when the note grows past the configured size; prefer a summary + index note with detail in children (linked via `parent`).")]
    public SaveResult Invoke(
        [Description("The file name to append to.")]
        string name,
        [Description("Content to append to the end of the current version.")]
        string content,
        [Description("The version this append is derived from (for conflict detection).")]
        int base_version,
        [Description("The project namespace. If omitted, it is resolved from the environment / working directory.")]
        string project = null)
        => common.Run("vault_append", info =>
        {
            var resolvedProject = resolver.Resolve(project);
            info.Project = resolvedProject.Value;
            var fileName = FileName.Parse(name);
            info.Name = fileName.Value;
            info.ContentSizeBytes = content is null ? 0 : Encoding.UTF8.GetByteCount(content);

            var committed = service.Append(resolvedProject, fileName, content, base_version);
            info.CommittedChars = committed.ContentChars;
            return new SaveResult { Version = committed.Version, ContentHash = committed.Hash.Hex, SplitHint = committed.SplitHint };
        });
}