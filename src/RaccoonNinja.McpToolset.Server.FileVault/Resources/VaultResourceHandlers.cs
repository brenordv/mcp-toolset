using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using RaccoonNinja.McpToolset.Server.FileVault.Domain;
using RaccoonNinja.McpToolset.Server.FileVault.Errors;
using RaccoonNinja.McpToolset.Server.FileVault.Services;

namespace RaccoonNinja.McpToolset.Server.FileVault.Resources;

/// <summary>
/// Dynamic <c>vault://&lt;project&gt;/&lt;name&gt;</c> resources: listing exposes every active
/// note across all projects; reading returns the current content. Registered as handlers (not
/// attributes) because the resource set is the live vault contents.
/// </summary>
public static class VaultResourceHandlers
{
    /// <summary>Build the resource list from every active file across all projects.</summary>
    /// <param name="service">The vault service.</param>
    /// <returns>The MCP resource list.</returns>
    public static ListResourcesResult ListResources(VaultService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        var items = service.List(project: null, tags: null, query: null);
        return new ListResourcesResult
        {
            Resources =
            [
                .. items
                    .Select(item => new Resource
                    {
                        Uri = $"vault://{item.Project}/{item.Name}", Name = item.Name, Description = item.Summary,
                    })
            ],
        };
    }

    /// <summary>Read one resource's current content.</summary>
    /// <param name="service">The vault service.</param>
    /// <param name="uri">The <c>vault://</c> URI.</param>
    /// <returns>The MCP resource contents.</returns>
    public static ReadResourceResult ReadResource(VaultService service, string uri)
    {
        ArgumentNullException.ThrowIfNull(service);
        try
        {
            var (project, name) = ParseVaultUri(uri);
            var (_, content, _) = service.Get(project, name, version: null);
            return new ReadResourceResult
            {
                Contents =
                [
                    new TextResourceContents { Uri = uri, Text = content, },
                ],
            };
        }
        catch (VaultException ex)
        {
            // Same domain-error translation as the tools: the client gets the structured error body
            // (code + payload) instead of an opaque internal error, and the SDK's failed-handler
            // log carries an McpException, whose text the log formatter refuses.
            throw new McpException(ErrorMapping.ToErrorJson(ex));
        }
    }

    /// <summary>
    /// Parse a <c>vault://&lt;project&gt;/&lt;name&gt;</c> URI into validated components. The
    /// project may itself contain <c>/</c> (monorepo <c>repo/app</c>), so the file name is taken
    /// as the final path segment; both halves re-run the standard name validation.
    /// </summary>
    /// <param name="uri">The raw URI.</param>
    /// <returns>The validated project and name.</returns>
    /// <exception cref="VaultException">Thrown with <see cref="VaultErrorCode.InvalidName"/> for a malformed URI.</exception>
    private static (ProjectName Project, FileName Name) ParseVaultUri(string uri)
    {
        const string prefix = "vault://";
        if (uri is null || !uri.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw VaultException.InvalidName($"'{uri}' is not a vault:// URI");
        }

        var rest = uri[prefix.Length..];
        var lastSlash = rest.LastIndexOf('/');
        return lastSlash >= 0
            ? (ProjectName.Parse(rest[..lastSlash]), FileName.Parse(rest[(lastSlash + 1)..]))
            : throw VaultException.InvalidName($"vault URI '{uri}' must be vault://<project>/<name>");
    }
}