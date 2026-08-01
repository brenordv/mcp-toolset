using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Models;

/// <summary>
/// The payload of <c>describe_scope</c>: the sandbox an agent operates in, described without any
/// absolute path. It names the base root by basename, explains the <c>cwd</c> scope model, and lists the
/// ignore tiers, the secret denylist, and every effective cap so the agent can shape its calls up front.
/// </summary>
public sealed record ScopeInfo
{
    /// <summary>The base root's basename (never an absolute path).</summary>
    [JsonPropertyName("base_root")]
    public string BaseRoot { get; init; }

    /// <summary>How scoping and paths work: <c>cwd</c> scoping, cwd-relative paths, the whole-base heavy path, and the scoped-ancestor-ignore caveat.</summary>
    [JsonPropertyName("scope_model")]
    public string ScopeModel { get; init; }

    /// <summary>The effective default-ignore patterns (empty when the tier is disabled).</summary>
    [JsonPropertyName("default_ignore")]
    public IReadOnlyList<string> DefaultIgnore { get; init; } = [];

    /// <summary>The project ignore-file kinds honored, least specific first.</summary>
    [JsonPropertyName("ignore_files")]
    public IReadOnlyList<string> IgnoreFiles { get; init; } = [];

    /// <summary>The non-overridable secret denylist patterns (built-ins plus any operator extensions).</summary>
    [JsonPropertyName("denylist")]
    public IReadOnlyList<string> Denylist { get; init; } = [];

    /// <summary>The default output encoding (UTF-8, no BOM).</summary>
    [JsonPropertyName("default_encoding")]
    public string DefaultEncoding { get; init; }

    /// <summary>The unit a reported column counts.</summary>
    [JsonPropertyName("column_unit")]
    public string ColumnUnit { get; init; }

    /// <summary>Whether denylisted matches are omitted from output (always true).</summary>
    [JsonPropertyName("denylisted_omitted")]
    public bool DenylistedOmitted { get; init; }

    /// <summary>The effective caps.</summary>
    [JsonPropertyName("caps")]
    public ScopeCaps Caps { get; init; }
}