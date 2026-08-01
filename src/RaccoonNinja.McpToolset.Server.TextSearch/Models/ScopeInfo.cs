using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Models;

/// <summary>
/// The payload of <c>describe_scope</c>: the sandbox an agent operates in, described without any
/// absolute path. Roots are listed by name and kind; every effective cap is reported so the agent can
/// shape its calls up front.
/// </summary>
public sealed record ScopeInfo
{
    /// <summary>The allowed roots, by name and kind (never an absolute path).</summary>
    [JsonPropertyName("roots")]
    public IReadOnlyList<RootDescriptor> Roots { get; init; } = [];

    /// <summary>The ignore-file kinds honored (off by default only via <c>include_ignored</c>).</summary>
    [JsonPropertyName("ignore_files")]
    public IReadOnlyList<string> IgnoreFiles { get; init; } = [];

    /// <summary>The non-overridable secret denylist patterns.</summary>
    [JsonPropertyName("denylist_patterns")]
    public IReadOnlyList<string> DenylistPatterns { get; init; } = [];

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