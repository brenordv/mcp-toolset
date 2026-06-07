namespace RaccoonNinja.McpToolset.Server.GitOps.Security;

/// <summary>
/// Typed declaration of a single git invocation, prior to argv assembly. Tools
/// populate this; <see cref="GitCommandBuilder.Build"/> is the only code permitted
/// to serialize it into an argv list. Fields tagged "user" are treated as untrusted
/// and validated by the builder.
/// </summary>
public sealed class GitIntent
{
    public string Subcommand { get; init; }
    public string RepoRoot { get; init; }
    public string SubSubcommand { get; init; }

    /// <summary>Server-built flags (constant, never user input).</summary>
    public IList<string> Flags { get; init; } = [];

    /// <summary>Attached-form options whose value originated from user input. Emitted as <c>--name=value</c> / <c>-Xvalue</c>.</summary>
    public IList<AttachedOption> AttachedOptions { get; init; } = [];

    /// <summary>Already-verified refs (caller resolved via <c>rev-parse --verify</c>).</summary>
    public IList<string> VerifiedRefs { get; init; } = [];

    /// <summary>Positional non-ref args (server data: pretty format strings, refname patterns, etc.).</summary>
    public IList<string> PositionalServerArgs { get; init; } = [];

    /// <summary>Pathspecs supplied by the user; confined by the tool before populating this list.</summary>
    public IList<string> Pathspecs { get; init; } = [];

    /// <summary>Map <c>argv_index → param_name</c> populated during build for masked DEBUG logging.</summary>
    public IDictionary<int, string> Provenance { get; set; } = new Dictionary<int, string>();
}

/// <summary>An attached-form option pair: <c>("--author", "value")</c> → <c>--author=value</c>.</summary>
public readonly record struct AttachedOption(string Flag, string Value);