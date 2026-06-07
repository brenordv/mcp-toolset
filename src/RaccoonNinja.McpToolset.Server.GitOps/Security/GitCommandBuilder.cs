using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;

namespace RaccoonNinja.McpToolset.Server.GitOps.Security;

/// <summary>
/// Sole producer of git argv. Tools declare typed <see cref="GitIntent"/> instances;
/// this class is the only code that turns them into a process argv list.
/// </summary>
public static class GitCommandBuilder
{
    private static readonly IReadOnlyList<string> GlobalFlags =
    [
        "--no-pager",
        "--literal-pathspecs"
    ];

    /// <summary><c>--no-textconv</c> is accepted by every diff-bearing subcommand (incl. grep). <c>--no-ext-diff</c> is NOT a grep flag.</summary>
    private static readonly IReadOnlyList<string> DiffBearingFlags =
    [
        "--no-ext-diff",
        "--no-textconv"
    ];

    private static readonly IReadOnlyList<string> GrepDiffFlags = ["--no-textconv"];

    /// <summary>Fixed <c>-c</c> neutralizers: constant values, server-emitted, never user input.</summary>
    private static readonly IReadOnlyList<(string Key, string Value)> FixedConfig =
    [
        ("core.fsmonitor", string.Empty),
        ("core.hooksPath", "/nonexistent/git-check-hooks-disabled"),
        ("core.pager", "cat"),
        ("gc.auto", "0"),
        ("credential.helper", string.Empty),
        ("core.askpass", string.Empty),
        ("core.quotePath", "false")
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
    public static (IList<string> Argv, IDictionary<string, string> Env) Build(GitIntent intent, string gitExecutable = "git")
    {
        ArgumentNullException.ThrowIfNull(intent);

        var argv = new List<string> { gitExecutable };
        var provenance = new Dictionary<int, string>();

        // 1. Fixed hardening config.
        foreach (var (key, value) in FixedConfig)
        {
            argv.Add("-c");
            argv.Add($"{key}={value}");
        }

        // 2. Global flags.
        argv.AddRange(GlobalFlags);

        // 3. -C <repo>.
        argv.Add("-C");
        provenance[argv.Count] = "repo_root";
        argv.Add(intent.RepoRoot);

        // 4. Subcommand + optional sub-subcommand.
        argv.Add(intent.Subcommand);
        if (!string.IsNullOrEmpty(intent.SubSubcommand))
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

        // 8. End-of-options barrier + verified refs as positional args.
        argv.Add("--end-of-options");
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
                ArgumentValidation.RejectIfUnsafeValue("path", spec);
                argv.Add(spec);
                provenance[argv.Count - 1] = "path";
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

    /// <summary>Return a copy of <paramref name="argv"/> with user-originating tokens replaced by their parameter name.</summary>
    public static IList<string> MaskedForLog(IList<string> argv, GitIntent intent)
    {
        ArgumentNullException.ThrowIfNull(argv);
        ArgumentNullException.ThrowIfNull(intent);

        var masked = new List<string>(argv);
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