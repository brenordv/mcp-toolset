using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.FileVault.Domain;
using RaccoonNinja.McpToolset.Server.FileVault.Extensions;
using RaccoonNinja.McpToolset.Server.FileVault.Models;
using RaccoonNinja.McpToolset.Server.FileVault.Services;
using RaccoonNinja.McpToolset.Server.FileVault.Storage;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tools;

/// <summary>The <c>vault_save</c> tool: the first-class versioned write.</summary>
[McpServerToolType]
public sealed class VaultSaveTool(ToolCommon common, VaultService service, ProjectResolver resolver)
{
    [McpServerTool(Name = "vault_save", UseStructuredContent = true)]
    [Description("Save content under a flat name, creating a new immutable version. Omit `base_version` only for the first-ever save of a name; otherwise pass the version you last read so stale writes are rejected as conflicts. To change only the summary, tags, or parent of an existing note, use `vault_set_meta` instead — you do not need to resend `content`. Keep large notes as a summary + index and put the detail in child notes (linked via `parent`); the result carries a `hint` when a note grows past the configured size.")]
    public SaveResult Invoke(
        [Description("The flat name to save under (e.g. `draft-letter`). No paths.")]
        string name,
        [Description("The full content to store as a new version.")]
        string content,
        [Description("A one-line, human-readable summary of this version (required; written by the assistant).")]
        string summary,
        [Description("The version this write is derived from. Omit **only** for the first-ever save of this name.")]
        int? base_version = null,
        [Description("The project namespace. If omitted, it is resolved from the environment / working directory.")]
        string project = null,
        [Description("Optional tags for filtering and grouping.")]
        string[] tags = null,
        [Description("Content format: one of `text`, `markdown`, `json`, `yaml`. Defaults to `text`.")]
        string format = null,
        [Description("Optional parent file name (same project) to link this note under. To change only the parent of an existing note (or to detach one), use `vault_set_meta` instead.")]
        string parent = null)
        => common.Run("vault_save", info =>
        {
            var resolvedProject = resolver.Resolve(project);
            info.Project = resolvedProject.Value;
            var fileName = FileName.Parse(name);
            info.Name = fileName.Value;
            info.ContentSizeBytes = content is null ? 0 : Encoding.UTF8.GetByteCount(content);

            var parentUpdate = parent is null
                ? ParentUpdate.Leave
                : ParentUpdate.Set(FileName.Parse(parent).Value);

            var committed = service.Save(
                resolvedProject,
                fileName,
                content,
                summary,
                base_version,
                tags,
                VaultFormatExtensions.ParseVaultFormat(format),
                parentUpdate);

            info.CommittedChars = committed.ContentChars;
            return new SaveResult { Version = committed.Version, ContentHash = committed.Hash.Hex, SplitHint = committed.SplitHint };
        });
}