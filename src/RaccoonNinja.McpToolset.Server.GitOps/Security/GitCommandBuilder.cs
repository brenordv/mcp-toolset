using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;

namespace RaccoonNinja.McpToolset.Server.GitOps.Security;

/// <summary>
/// Sole producer of git argv. Tools declare typed <see cref="GitIntent"/> instances;
/// this class is the only code that turns them into a process argv list. Each pathspec
/// is rendered with a per-item magic prefix chosen by the server (<see cref="PathspecMagic"/>).
/// </summary>
public static class GitCommandBuilder
{
    private const string LiteralMagic = ":(literal)";
    private const string ExcludeMagic = ":(exclude)";

    private static readonly IReadOnlyList<string> GlobalFlags =
    [
        "--no-pager"
    ];

    /// <summary><c>--no-textconv</c> is accepted by every diff-bearing subcommand (incl. grep). <c>--no-ext-diff</c> is NOT a grep flag.</summary>
    private static readonly IReadOnlyList<string> DiffBearingFlags =
    [
        "--no-ext-diff",
        "--no-textconv"
    ];

    private static readonly IReadOnlyList<string> GrepDiffFlags = ["--no-textconv"];

    /// <summary>
    /// Fixed <c>-c</c> neutralizers: constant values, server-emitted, never user input. git reads <c>-c</c> values after
    /// every config file, so these beat the user's and the repository's config. <c>log.showSignature=false</c> keeps a
    /// repository's config from making <c>git log</c>, <c>git show</c>, <c>git reflog</c>, or the <c>git log</c> that
    /// <c>git stash list</c> starts verify signed commits, which runs the program <c>gpg.program</c>,
    /// <c>gpg.ssh.program</c>, or <c>gpg.x509.program</c> names. <c>log.follow=false</c> keeps <c>git log</c> with one
    /// pathspec from following renames unless the call passes <c>--follow</c>, which sets the mode directly.
    /// <c>fetch.bundleURI=</c> reaches the <c>git fetch</c> a lazy fetch starts on a git that does not check
    /// <c>GIT_NO_LAZY_FETCH</c>: from git 2.40.0 that fetch first downloads the bundle the key names, before any protocol
    /// check, and git skips an empty bundle URI.
    /// </summary>
    private static readonly IReadOnlyList<(string Key, string Value)> FixedConfig =
    [
        ("core.fsmonitor", string.Empty),
        ("core.hooksPath", "/nonexistent/git-check-hooks-disabled"),
        ("core.pager", "cat"),
        ("gc.auto", "0"),
        ("credential.helper", string.Empty),
        ("core.askpass", string.Empty),
        ("core.quotePath", "false"),
        ("log.showSignature", "false"),
        ("log.follow", "false"),
        ("fetch.bundleURI", string.Empty)
    ];

    private static readonly HashSet<string> DiffBearingSubcommands = new(StringComparer.Ordinal)
    {
        "diff", "show", "log", "blame", "grep", "stash",
    };

    /// <summary>Return the constant hardening argv prefix (<c>-c k=v ...</c> + global flags).</summary>
    public static IList<string> HardeningArgvPrefix()
    {
        var prefix = new List<string>(GlobalFlags.Count + FixedConfig.Count * 2);
        foreach (var (key, value) in FixedConfig)
        {
            prefix.Add("-c");
            prefix.Add($"{key}={value}");
        }
        foreach (var flag in GlobalFlags) prefix.Add(flag);
        return prefix;
    }

    /// <summary>Convert a <see cref="GitIntent"/> into a hardened argv list plus the child env.</summary>
    /// <param name="intent">The invocation to serialize.</param>
    /// <param name="gitExecutable">The absolute path of the git executable, written as <c>argv[0]</c>.</param>
    /// <param name="filterDrivers">
    /// Filter driver names to pin, as <see cref="FilterDriverPins.ParseDriverNames"/> returns them; <c>null</c> pins none.
    /// Each name comes from the config git reads, which includes the repository's own, so its argv tokens are masked in logs.
    /// </param>
    /// <returns>The argv, executable first, and the child environment.</returns>
    public static (IList<string> Argv, IDictionary<string, string> Env) Build(
        GitIntent intent,
        string gitExecutable,
        IReadOnlyCollection<string> filterDrivers = null)
    {
        ArgumentNullException.ThrowIfNull(intent);

        var argv = new List<string> { gitExecutable };
        var provenance = new Dictionary<int, string>();

        // 1. The intent's own config pins, then the fixed hardening config, which wins over a pin that repeats one of its
        // keys, then the pins for each filter driver the config git reads names.
        if (intent.ConfigPins is not null)
        {
            foreach (var (key, value) in intent.ConfigPins)
            {
                argv.Add("-c");
                argv.Add($"{key}={value}");
            }
        }

        foreach (var (key, value) in FixedConfig)
        {
            argv.Add("-c");
            argv.Add($"{key}={value}");
        }

        if (filterDrivers is not null)
        {
            foreach (var driver in filterDrivers)
            {
                foreach (var (key, value) in FilterDriverPins.PinsFor(driver))
                {
                    argv.Add("-c");
                    provenance[argv.Count] = "filter_driver";
                    argv.Add($"{key}={value}");
                }
            }
        }

        // 2. Global flags.
        argv.AddRange(GlobalFlags);

        // 3. -C <repo>.
        argv.Add("-C");
        provenance[argv.Count] = "repo_root";
        argv.Add(intent.RepoRoot);

        // 4. Subcommand + optional sub-subcommand.
        argv.Add(intent.Subcommand);
        if (!string.IsNullOrWhiteSpace(intent.SubSubcommand))
        {
            argv.Add(intent.SubSubcommand);
        }

        // 5. Diff-bearing hardening.
        if (intent.Subcommand == "grep")
        {
            argv.AddRange(GrepDiffFlags);
        }
        else if (DiffBearingSubcommands.Contains(intent.Subcommand))
        {
            argv.AddRange(DiffBearingFlags);
        }

        // 6. Server-controlled flags.
        argv.AddRange(intent.Flags);

        // 7. Attached-form user options.
        foreach (var option in intent.AttachedOptions)
        {
            ArgumentValidation.RejectIfUnsafeValue(option.Flag, option.Value);
            string token;
            if (option.Flag.StartsWith("--", StringComparison.Ordinal))
            {
                token = $"{option.Flag}={option.Value}";
            }
            else
            {
                token = option.Flag.StartsWith('-') && option.Flag.Length == 2
                    ? $"{option.Flag}{option.Value}"
                    : throw new RejectedArgumentException(
                    $"attached option '{option.Flag}' has an unsupported shape",
                    new Dictionary<string, object> { ["param"] = option.Flag });
            }
            argv.Add(token);
            provenance[argv.Count - 1] = option.Flag.TrimStart('-');
        }

        // 8. Optional end-of-options barrier, then verified refs as positional args.
        // git grep is parsed with PARSE_OPT_KEEP_DASHDASH and, on git builds where its parser leaves a
        // literal "--end-of-options" in argv (the same class of bug fixed for checkout/reset in git
        // 2.43.1), that leftover token is taken as a bogus revision and grep aborts via verify_filename()
        // with exit 128. grep never needs the barrier: its pattern is a glued "-e<pattern>" token, its
        // refs are RefVerifier-resolved object names, and its pathspecs sit behind "--". So skip it for
        // grep, and for every other subcommand emit it only when a positional actually follows it (a
        // trailing marker with nothing after it is pointless and just as fragile on those parsers).
        var hasPositionals = intent.VerifiedRefs.Count > 0 || intent.PositionalServerArgs.Count > 0;
        if (intent.Subcommand != "grep" && hasPositionals)
        {
            argv.Add("--end-of-options");
        }
        foreach (var refToken in intent.VerifiedRefs)
        {
            argv.Add(refToken);
            provenance[argv.Count - 1] = "ref";
        }

        // 9. Server-controlled positional args.
        argv.AddRange(intent.PositionalServerArgs);

        // 10. -- separator then pathspecs.
        if (intent.Pathspecs.Count > 0)
        {
            argv.Add("--");
            foreach (var spec in intent.Pathspecs)
            {
                if (string.IsNullOrWhiteSpace(spec.Value))
                {
                    throw new RejectedArgumentException(
                        "pathspec must not be empty",
                        new Dictionary<string, object> { ["param"] = spec.Param });
                }

                ArgumentValidation.RejectIfUnsafeValue(spec.Param, spec.Value);
                argv.Add(Render(spec));
                provenance[argv.Count - 1] = spec.Param;
            }
        }

        foreach (var kvp in intent.Provenance)
        {
            provenance[kvp.Key] = kvp.Value;
        }
        intent.Provenance = provenance;

        var env = EnvironmentBuilder.Build();
        return (argv, env);
    }

    /// <summary>Render one pathspec token. A bare value must not start with ':' because git reads pathspec magic only from a leading colon.</summary>
    /// <param name="spec">A pathspec whose value is already known to be non-empty.</param>
    /// <returns>The argv token: the server's magic prefix followed by the value, or the bare value.</returns>
    /// <exception cref="RejectedArgumentException">A bare value starts with ':', or the magic was never assigned.</exception>
    private static string Render(Pathspec spec)
        => spec.Magic switch
        {
            PathspecMagic.Literal => LiteralMagic + spec.Value,
            PathspecMagic.Exclude => ExcludeMagic + spec.Value,
            PathspecMagic.None when spec.Value[0] == ':' => throw new RejectedArgumentException(
                "pathspec must not begin with ':'",
                new Dictionary<string, object> { ["param"] = spec.Param }),
            PathspecMagic.None => spec.Value,
            _ => throw new RejectedArgumentException(
                "pathspec magic is not set",
                new Dictionary<string, object> { ["param"] = spec.Param }),
        };

    /// <summary>
    /// Return a copy of <paramref name="argv"/> with user-originating tokens replaced by their parameter name, and the
    /// executable shown as <c>git</c>, never its path.
    /// </summary>
    public static IList<string> MaskedForLog(IList<string> argv, GitIntent intent)
    {
        ArgumentNullException.ThrowIfNull(argv);
        ArgumentNullException.ThrowIfNull(intent);

        var masked = new List<string>(argv);
        if (masked.Count > 0)
        {
            masked[0] = GitExecutableLocator.DisplayName;
        }

        foreach (var (index, paramName) in intent.Provenance)
        {
            if (index < 0 || index >= masked.Count) continue;
            var token = masked[index];
            if (token.Contains('=') && token.StartsWith("--", StringComparison.Ordinal))
            {
                var eq = token.IndexOf('=');
                masked[index] = $"{token[..eq]}=<{paramName}>";
            }
            else
            {
                masked[index] = $"<{paramName}>";
            }
        }
        return masked;
    }
}