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

    /// <summary>Pathspecs supplied by the user, each tagged with the magic the builder prefixes. The tool confines or validates every value before adding it here.</summary>
    public IList<Pathspec> Pathspecs { get; init; } = [];

    /// <summary>
    /// <c>-c</c> pairs for this one invocation. Server constants only, never user input: a value here is config git
    /// obeys, and keys such as <c>core.fsmonitor</c> or <c>diff.external</c> run programs. The builder writes them before
    /// the fixed hardening config, so a pin that repeats one of its keys cannot beat it. <c>null</c> means none.
    /// </summary>
    public IReadOnlyList<(string Key, string Value)> ConfigPins { get; init; }

    /// <summary>Map <c>argv_index → param_name</c> populated during build for masked DEBUG logging.</summary>
    public IDictionary<int, string> Provenance { get; set; } = new Dictionary<int, string>();
}

/// <summary>An attached-form option pair: <c>("--author", "value")</c> → <c>--author=value</c>.</summary>
public readonly record struct AttachedOption(string Flag, string Value);