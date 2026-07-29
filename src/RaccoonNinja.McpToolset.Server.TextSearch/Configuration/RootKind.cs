namespace RaccoonNinja.McpToolset.Server.TextSearch.Configuration;

/// <summary>What a configured root is for. Package roots are opt-in and require a narrowed search.</summary>
public enum RootKind
{
    /// <summary>A project folder the user works in; searched by default.</summary>
    Workspace = 1,

    /// <summary>A locally cached dependency source (crates, nuget, npm, ...); only searched on request.</summary>
    Package = 2,
}