using System.ComponentModel;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.FileVault.Domain;
using RaccoonNinja.McpToolset.Server.FileVault.Extensions;
using RaccoonNinja.McpToolset.Server.FileVault.Models;
using RaccoonNinja.McpToolset.Server.FileVault.Services;
using RaccoonNinja.McpToolset.Server.FileVault.Storage;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tools;

/// <summary>The <c>vault_get</c> tool: fetch content + metadata + the conflict token.</summary>
[McpServerToolType]
public sealed class VaultGetTool(ToolCommon common, VaultService service, ProjectResolver resolver)
{
    [McpServerTool(Name = "vault_get", UseStructuredContent = true)]
    [Description("Fetch a file's content and metadata. Returns `current_version`, and pass it back as `base_version` on your next write.")]
    public GetResult Invoke(
        [Description("The file name to fetch.")]
        string name,
        [Description("The project namespace. If omitted, it is resolved from the environment / working directory.")]
        string project = null,
        [Description("A specific version to fetch. Defaults to the current version.")]
        int? version = null)
        => common.Run("vault_get", info =>
        {
            var resolvedProject = resolver.Resolve(project);
            info.Project = resolvedProject.Value;
            var fileName = FileName.Parse(name);
            info.Name = fileName.Value;

            var (record, content, children) = service.Get(resolvedProject, fileName, version);
            return new GetResult
            {
                Content = content,
                Summary = record.Summary,
                Tags = record.Tags,
                Format = record.Format.ToWireString(),
                Version = record.Version,
                ContentHash = record.Hash.Hex,
                CurrentVersion = record.CurrentVersion,
                Archived = record.State == FileState.Archived,
                Parent = record.Parent,
                Children = children.Select(child => new ChildItem { Name = child.Name, Summary = child.Summary }).ToList(),
            };
        });
}