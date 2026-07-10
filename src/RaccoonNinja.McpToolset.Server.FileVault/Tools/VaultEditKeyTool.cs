using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.FileVault.Domain;
using RaccoonNinja.McpToolset.Server.FileVault.Models;
using RaccoonNinja.McpToolset.Server.FileVault.Services;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tools;

/// <summary>The <c>vault_edit_key</c> tool: JSON/YAML key-path editing.</summary>
[McpServerToolType]
public sealed class VaultEditKeyTool(ToolCommon common, VaultService service, ProjectResolver resolver)
{
    [McpServerTool(Name = "vault_edit_key", UseStructuredContent = true)]
    [Description("Set a value at a dotted key path in a JSON or YAML file without rewriting the rest. JSON/YAML files only.")]
    public SaveResult Invoke(
        [Description("The file name to edit.")]
        string name,
        [Description("Dotted path to the value to set (e.g. `server.port`).")]
        string key_path,
        [Description("The new value (any JSON value).")]
        JsonElement value,
        [Description("The version this edit is derived from (for conflict detection).")]
        int base_version,
        [Description("The project namespace. If omitted, it is resolved from the environment / working directory.")]
        string project = null)
        => common.Run("vault_edit_key", info =>
        {
            var resolvedProject = resolver.Resolve(project);
            info.Project = resolvedProject.Value;
            var fileName = FileName.Parse(name);
            info.Name = fileName.Value;

            var committed = service.EditKey(resolvedProject, fileName, key_path, value, base_version);
            info.CommittedChars = committed.ContentChars;
            return new SaveResult { Version = committed.Version, ContentHash = committed.Hash.Hex, SplitHint = committed.SplitHint };
        });
}