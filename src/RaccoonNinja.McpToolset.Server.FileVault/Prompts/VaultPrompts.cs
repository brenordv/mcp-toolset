using System.ComponentModel;
using System.Globalization;
using System.Text;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.FileVault.Domain;
using RaccoonNinja.McpToolset.Server.FileVault.Errors;
using RaccoonNinja.McpToolset.Server.FileVault.Services;

namespace RaccoonNinja.McpToolset.Server.FileVault.Prompts;

/// <summary>
/// The two vault prompts. Each returns a single user-role message whose text matches the Rust
/// server verbatim.
/// </summary>
[McpServerPromptType]
public sealed class VaultPrompts(VaultService service, ProjectResolver resolver)
{
    /// <summary>Continue a draft stored in the vault from its current content.</summary>
    /// <param name="name">The draft note name.</param>
    /// <param name="project">Optional project; resolved from the environment when omitted.</param>
    /// <returns>The user-role prompt text.</returns>
    [McpServerPrompt(Name = "continue-draft")]
    [Description("Continue a draft stored in the vault: loads the current content and asks the model to keep writing from where it left off.")]
    public string ContinueDraft(
        [Description("The name of the draft to continue.")]
        string name,
        [Description("The project namespace (optional; resolved from the environment if omitted).")]
        string project = null)
    {
        try
        {
            var resolvedProject = resolver.Resolve(project);
            var fileName = FileName.Parse(name);
            var (record, content, _) = service.Get(resolvedProject, fileName, version: null);
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Here is the current draft (version {record.Version}). Continue it from where it leaves off, "
                + $"preserving voice and intent. Do not repeat what is already written.\n\n{content}");
        }
        catch (VaultException ex)
        {
            // Same domain-error translation as the tools; also keeps the SDK's failed-handler log from
            // carrying a raw VaultException (whose message the log formatter refuses).
            throw new McpException(ErrorMapping.ToErrorJson(ex));
        }
    }

    /// <summary>Summarize the current contents of the vault (optionally one project).</summary>
    /// <param name="project">Optional project restriction.</param>
    /// <returns>The user-role prompt text.</returns>
    [McpServerPrompt(Name = "summarize-vault")]
    [Description("Summarize the current contents of the vault (optionally for one project) so the user can see what is available at a glance.")]
    public string SummarizeVault(
        [Description("Restrict the summary to a single project (optional).")]
        string project = null)
    {
        try
        {
            // Like vault_list, an omitted project means "across ALL projects"; no inference.
            var resolvedProject = project is null ? null : resolver.Resolve(project);
            var items = service.List(resolvedProject, tags: null, query: null);

            var body = new StringBuilder("Summarize the following vault items for the user, grouping related ones:\n\n");
            if (items.Count == 0)
            {
                body.Append("(the vault is empty)");
            }

            foreach (var item in items)
            {
                body.Append(CultureInfo.InvariantCulture, $"- {item.Project}/{item.Name} (v{item.CurrentVersion}): {item.Summary}\n");
            }

            return body.ToString();
        }
        catch (VaultException ex)
        {
            throw new McpException(ErrorMapping.ToErrorJson(ex));
        }
    }
}