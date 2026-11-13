using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.GitOps.Models;

/// <summary>The payload <c>git_version</c> returns.</summary>
public sealed record GitVersionResult
{
    /// <summary>The text after <c>git version </c> in the output of the git the server runs, for example <c>2.56.0</c>.</summary>
    [JsonPropertyName("version")]
    public string Version { get; init; }
}