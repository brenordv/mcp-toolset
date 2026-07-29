using RaccoonNinja.McpToolset.Files.Security;
using RaccoonNinja.McpToolset.Files.Selection;
using RaccoonNinja.McpToolset.Server.TextSearch.Content;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Configuration;

/// <summary>
/// One configured, confined root: its agent-facing name, its kind, and the per-root collaborators
/// (confiner, selection, gated reader) the tools use. The absolute path lives only inside the confiner
/// and is never surfaced.
/// </summary>
public sealed class RootSpec
{
    /// <summary>Create a root spec.</summary>
    /// <param name="name">The stable, agent-facing name (alias or basename).</param>
    /// <param name="kind">Whether this is a workspace or package root.</param>
    /// <param name="confinement">The confiner bound to this root.</param>
    /// <param name="selection">The selection service over this root.</param>
    /// <param name="reader">The gated content reader over this root.</param>
    public RootSpec(string name, RootKind kind, RootConfinement confinement, FileSelection selection, GatedFileReader reader)
    {
        Name = name;
        Kind = kind;
        Confinement = confinement;
        Selection = selection;
        Reader = reader;
    }

    /// <summary>The stable name the agent uses to target this root.</summary>
    public string Name { get; }

    /// <summary>Whether this is a workspace or package root.</summary>
    public RootKind Kind { get; }

    /// <summary>The confiner bound to this root.</summary>
    public RootConfinement Confinement { get; }

    /// <summary>The selection service over this root.</summary>
    public FileSelection Selection { get; }

    /// <summary>The gated content reader over this root.</summary>
    public GatedFileReader Reader { get; }
}