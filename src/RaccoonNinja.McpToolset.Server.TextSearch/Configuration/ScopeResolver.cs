using RaccoonNinja.McpToolset.Files.Security;
using RaccoonNinja.McpToolset.Files.Selection;
using RaccoonNinja.McpToolset.Server.TextSearch.Content;
using RaccoonNinja.McpToolset.Server.TextSearch.Errors;
using RaccoonNinja.McpToolset.Server.TextSearch.Logging;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Configuration;

/// <summary>
/// Resolves each call's effective scope under the one base root. The base root is the hard confinement
/// ceiling; a per-call <c>cwd</c> narrows the walk to a subdirectory and becomes the effective root (paths
/// are then relative to it), while an omitted <c>cwd</c> uses the whole base. Every <c>cwd</c> is confined
/// by the same physical-resolution control the base root gets, rejected if it lands on or inside a
/// denylisted or reparent-unsafe directory, and re-bound under the base after the effective confiner is
/// built so a resolve-then-construct swap cannot escape the ceiling. Replaces the old multi-named-root
/// registry.
/// </summary>
public sealed class ScopeResolver
{
    /// <summary>The environment variable naming the single base root (required).</summary>
    public const string EnvBaseRoot = "MCP_TEXTSEARCH_BASE_ROOT";

    /// <summary>The environment variable selecting or disabling the default ignore tier.</summary>
    public const string EnvDefaultIgnore = "MCP_TEXTSEARCH_DEFAULT_IGNORE";

    /// <summary>The environment variable naming additive secret-denylist patterns.</summary>
    public const string EnvExtraDeny = "MCP_TEXTSEARCH_EXTRA_DENY";

    private const string DisableDefaultIgnore = "off";
    private const char EntrySeparator = ';';

    private const string ReasonOutsideBase = "cwd_outside_base";
    private const string ReasonNotDirectory = "cwd_not_a_directory";
    private const string ReasonDenylisted = "cwd_denylisted";

    // The compiled-in default ignore tier: heavy build, dependency, and tool directories that a project
    // usually ignores anyway. Secret directories (.git and the like) belong to the denylist, not here.
    private static readonly string[] BuiltInDefaultIgnore =
    [
        "node_modules/", "bin/", "obj/", "target/", "dist/", "build/", "out/",
        ".venv/", "venv/", "__pycache__/", ".pytest_cache/", ".mypy_cache/",
        ".gradle/", ".idea/", ".vs/", ".next/", ".nuxt/", ".svelte-kit/",
        "coverage/", ".terraform/", ".turbo/", ".parcel-cache/",
    ];

    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private readonly RootConfinement _base;
    private readonly SecretDenylist _denylist;
    private readonly IgnoreRules _defaultIgnore;
    private readonly SearchConfig _config;
    private readonly CallScope _baseScope;

    private ScopeResolver(RootConfinement baseRoot, SecretDenylist denylist, IgnoreRules defaultIgnore, SearchConfig config)
    {
        _base = baseRoot;
        _denylist = denylist;
        _defaultIgnore = defaultIgnore;
        _config = config;
        _baseScope = BuildScope(baseRoot, ".");
    }

    /// <summary>The effective secret denylist (built-ins plus any operator extensions), for the scope description and DI.</summary>
    public ISecretDenylist Denylist => _denylist;

    /// <summary>The base root's basename, a human label for the scope description (never the absolute path).</summary>
    public string BaseRootName => Path.GetFileName(_base.CanonicalRoot.TrimEnd(Separators));

    /// <summary>The effective default-ignore patterns, empty when the tier is disabled.</summary>
    public IReadOnlyList<string> DefaultIgnorePatterns => _defaultIgnore.Patterns;

    /// <summary>An 8-char hash of the base canonical root, so scope logs correlate without leaking the path.</summary>
    public string RootHash => LogScrubbing.HashedValue(_base.CanonicalRoot);

    /// <summary>Build the resolver from the environment; fatal (via <see cref="SearchStartupException"/>) on bad config.</summary>
    /// <param name="config">The server config (supplies the read size cap).</param>
    /// <returns>The resolver.</returns>
    /// <exception cref="SearchStartupException">Thrown for a missing, unreadable, or dangerously broad base root, or invalid ignore/deny config.</exception>
    public static ScopeResolver Load(SearchConfig config)
        => Create(
            config,
            Environment.GetEnvironmentVariable(EnvBaseRoot),
            Environment.GetEnvironmentVariable(EnvDefaultIgnore),
            Environment.GetEnvironmentVariable(EnvExtraDeny));

    /// <summary>Build the resolver from explicit config strings (the env-free path, used by tests).</summary>
    /// <param name="config">The server config (supplies the read size cap).</param>
    /// <param name="baseRootValue">The base root path.</param>
    /// <param name="defaultIgnoreValue">The default-ignore selector: <c>off</c>, a file path, or null for the built-ins.</param>
    /// <param name="extraDenyValue">The additive deny patterns, or null.</param>
    /// <returns>The resolver.</returns>
    /// <exception cref="SearchStartupException">Thrown for a missing, unreadable, or dangerously broad base root, or invalid ignore/deny config.</exception>
    internal static ScopeResolver Create(SearchConfig config, string baseRootValue, string defaultIgnoreValue, string extraDenyValue)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (string.IsNullOrWhiteSpace(baseRootValue))
        {
            throw new SearchStartupException($"no base root set; set {EnvBaseRoot} to a directory to search");
        }

        var baseRoot = BuildConfinement(baseRootValue.Trim());
        var denylist = BuildDenylist(extraDenyValue);
        EnsureSafeBase(baseRoot, denylist);
        var defaultIgnore = BuildDefaultIgnore(defaultIgnoreValue);
        return new ScopeResolver(baseRoot, denylist, defaultIgnore, config);
    }

    /// <summary>Resolve a <c>cwd</c> argument to the effective call scope, confined under the base root.</summary>
    /// <param name="cwd">The absolute working directory to scope to, or null/blank for the whole base.</param>
    /// <returns>The resolved scope.</returns>
    /// <exception cref="TextSearchException">
    /// Thrown (as a refusal-tagged <c>InvalidArgument</c>) when the <c>cwd</c> escapes the base, is not a
    /// directory, or lands on or inside a protected directory. Every message is a fixed path-free constant.
    /// </exception>
    public CallScope Resolve(string cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd))
        {
            return _baseScope;
        }

        ConfinedPath confined;
        try
        {
            confined = _base.Confine(cwd, nameof(cwd));
        }
        catch (PathConfinementException)
        {
            throw OutsideBase();
        }

        if (!Directory.Exists(confined.RealPath))
        {
            throw new TextSearchException(
                ErrorCodes.InvalidArgument,
                "cwd is not an existing directory in the base root",
                refusalReason: ReasonNotDirectory);
        }

        if (_denylist.IsDeniedDirectory(confined.RelativePath) || IsReparentUnsafeLeaf(confined.RealPath))
        {
            throw new TextSearchException(
                ErrorCodes.InvalidArgument,
                "cwd is at or inside a protected directory",
                refusalReason: ReasonDenylisted);
        }

        var effective = new RootConfinement(confined.RealPath);
        if (!_base.ContainsPath(effective.CanonicalRoot))
        {
            throw OutsideBase();
        }

        return BuildScope(effective, confined.RelativePath);
    }

    private CallScope BuildScope(RootConfinement confinement, string scopeKey)
    {
        var selection = new FileSelection(confinement, _denylist, _defaultIgnore);
        var reader = new GatedFileReader(confinement, _denylist, _config.MaxFileBytes);
        return new CallScope(selection, reader, scopeKey);
    }

    /// <summary>Whether the effective root's leaf segment is the parent of a multi-segment marker (for example <c>.config</c>).</summary>
    private bool IsReparentUnsafeLeaf(string realPath)
    {
        var leaf = Path.GetFileName(realPath.TrimEnd(Separators));
        foreach (var segment in _denylist.ReparentUnsafeLeafSegments)
        {
            if (segment.Equals(leaf, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static TextSearchException OutsideBase()
        => new(ErrorCodes.InvalidArgument, "cwd is outside the configured base root", refusalReason: ReasonOutsideBase);

    private static RootConfinement BuildConfinement(string path)
    {
        try
        {
            return new RootConfinement(path);
        }
        catch (Exception ex) when (ex is ArgumentException or PathConfinementException)
        {
            throw new SearchStartupException($"{EnvBaseRoot} cannot be used: {ex.Message}");
        }
    }

    private static SecretDenylist BuildDenylist(string extraDenyValue)
    {
        var extras = ParseEntries(extraDenyValue);
        try
        {
            return new SecretDenylist(extras);
        }
        catch (Exception ex) when (ex is ArgumentException or RegexCompilationException)
        {
            throw new SearchStartupException($"{EnvExtraDeny} is invalid: {ex.Message}");
        }
    }

    private static IgnoreRules BuildDefaultIgnore(string value)
    {
        var trimmed = value?.Trim();
        if (string.Equals(trimmed, DisableDefaultIgnore, StringComparison.OrdinalIgnoreCase))
        {
            return IgnoreRules.Empty;
        }

        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            if (!File.Exists(trimmed))
            {
                throw new SearchStartupException($"{EnvDefaultIgnore} points to a file that does not exist");
            }

            try
            {
                return IgnoreRules.Parse(File.ReadAllLines(trimmed));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new SearchStartupException($"{EnvDefaultIgnore} file could not be read");
            }
        }

        return IgnoreRules.Parse(BuiltInDefaultIgnore);
    }

    /// <summary>Refuse a dangerously broad base root: a filesystem or drive root, the home directory, or one carrying a protected segment.</summary>
    private static void EnsureSafeBase(RootConfinement baseRoot, SecretDenylist denylist)
    {
        var canonical = baseRoot.CanonicalRoot;

        if (string.IsNullOrEmpty(Path.GetDirectoryName(canonical)))
        {
            throw new SearchStartupException($"{EnvBaseRoot} must not be a filesystem or drive root");
        }

        if (IsUserHome(canonical))
        {
            throw new SearchStartupException($"{EnvBaseRoot} must not be the user home directory");
        }

        // Check the whole base path at once, not segment by segment, so the multi-segment marker
        // (.config/gcloud) is caught when it sits in the base's own path, matching the per-call cwd check.
        var joined = string.Join('/', canonical.Split(Separators, StringSplitOptions.RemoveEmptyEntries));
        if (!string.IsNullOrEmpty(joined) && denylist.IsDeniedDirectory(joined))
        {
            throw new SearchStartupException($"{EnvBaseRoot} path must not contain a protected directory segment");
        }

        var leaf = Path.GetFileName(canonical.TrimEnd(Separators));
        foreach (var reparentUnsafe in denylist.ReparentUnsafeLeafSegments)
        {
            if (reparentUnsafe.Equals(leaf, StringComparison.OrdinalIgnoreCase))
            {
                throw new SearchStartupException($"{EnvBaseRoot} must not be placed directly on a protected parent directory");
            }
        }
    }

    private static bool IsUserHome(string canonical)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return !string.IsNullOrWhiteSpace(home)
            && canonical.TrimEnd(Separators).Equals(home.TrimEnd(Separators), PathComparison);
    }

    private static string[] ParseEntries(string raw)
        => string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(EntrySeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}