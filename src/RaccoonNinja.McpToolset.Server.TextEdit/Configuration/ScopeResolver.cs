using RaccoonNinja.McpToolset.Files.Security;
using RaccoonNinja.McpToolset.Files.Selection;
using RaccoonNinja.McpToolset.Server.TextEdit.Errors;
using RaccoonNinja.McpToolset.Server.TextEdit.Logging;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Configuration;

/// <summary>
/// Resolves each call's effective scope under the one base root. The base root is the hard confinement
/// ceiling; a per-call <c>cwd</c> narrows the walk to a subdirectory and becomes the effective root that
/// every write is confined to (the per-call write firewall), while an omitted <c>cwd</c> uses the whole
/// base. Every <c>cwd</c> is confined by the same physical-resolution control the base root gets, rejected
/// if it lands on or inside a denylisted or reparent-unsafe directory, and re-bound under the base after
/// the effective confiner is built so a resolve-then-construct swap cannot escape the ceiling. There are no
/// package roots: a write tool must never edit a read-only cache, so a <c>cwd</c> beginning with <c>@</c>
/// is an ordinary relative subdirectory. Replaces the old single-root registry.
/// </summary>
public sealed class ScopeResolver
{
    /// <summary>The environment variable naming the single base root (required).</summary>
    public const string EnvBaseRoot = "MCP_TEXTEDIT_BASE_ROOT";

    /// <summary>The environment variable selecting or disabling the default ignore tier.</summary>
    public const string EnvDefaultIgnore = "MCP_TEXTEDIT_DEFAULT_IGNORE";

    /// <summary>The environment variable naming additive secret-denylist patterns.</summary>
    public const string EnvExtraDeny = "MCP_TEXTEDIT_EXTRA_DENY";

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
    private readonly EditScope _baseScope;

    private ScopeResolver(RootConfinement baseRoot, SecretDenylist denylist, IgnoreRules defaultIgnore)
    {
        _base = baseRoot;
        _denylist = denylist;
        _defaultIgnore = defaultIgnore;
        _baseScope = BuildScope(baseRoot, ".");
    }

    /// <summary>The effective secret denylist (built-ins plus any operator extensions), for the scope description, the writer, and the undoer.</summary>
    public SecretDenylist Denylist => _denylist;

    /// <summary>The base root confiner, the ceiling for the writer, the undoer, and the journal directory.</summary>
    public RootConfinement BaseConfinement => _base;

    /// <summary>The base root's basename, a human label for the scope description (never the absolute path).</summary>
    public string BaseRootName => Path.GetFileName(_base.CanonicalRoot.TrimEnd(Separators));

    /// <summary>The effective default-ignore patterns, empty when the tier is disabled.</summary>
    public IReadOnlyList<string> DefaultIgnorePatterns => _defaultIgnore.Patterns;

    /// <summary>An 8-char hash of the base canonical root, so scope logs correlate without leaking the path.</summary>
    public string RootHash => LogScrubbing.HashedValue(_base.CanonicalRoot);

    /// <summary>Build the resolver from the environment; fatal (via <see cref="EditStartupException"/>) on bad config.</summary>
    /// <returns>The resolver.</returns>
    /// <exception cref="EditStartupException">Thrown for a missing, unreadable, or dangerously broad base root, or invalid ignore/deny config.</exception>
    public static ScopeResolver Load()
        => Create(
            Environment.GetEnvironmentVariable(EnvBaseRoot),
            Environment.GetEnvironmentVariable(EnvDefaultIgnore),
            Environment.GetEnvironmentVariable(EnvExtraDeny));

    /// <summary>Build the resolver from explicit config strings (the env-free path, used by tests).</summary>
    /// <param name="baseRootValue">The base root path.</param>
    /// <param name="defaultIgnoreValue">The default-ignore selector: <c>off</c>, a file path, or null for the built-ins.</param>
    /// <param name="extraDenyValue">The additive deny patterns, or null.</param>
    /// <returns>The resolver.</returns>
    /// <exception cref="EditStartupException">Thrown for a missing, unreadable, or dangerously broad base root, or invalid ignore/deny config.</exception>
    internal static ScopeResolver Create(string baseRootValue, string defaultIgnoreValue, string extraDenyValue)
    {
        if (string.IsNullOrWhiteSpace(baseRootValue))
        {
            throw new EditStartupException($"no base root set; set {EnvBaseRoot} to the directory to edit");
        }

        var baseRoot = BuildConfinement(baseRootValue.Trim());
        var denylist = BuildDenylist(extraDenyValue);
        EnsureSafeRoot(baseRoot, denylist);
        var defaultIgnore = BuildDefaultIgnore(defaultIgnoreValue);
        return new ScopeResolver(baseRoot, denylist, defaultIgnore);
    }

    /// <summary>Resolve a <c>cwd</c> argument to the effective call scope, confined under the base root.</summary>
    /// <param name="cwd">The absolute (or base-relative) working directory to scope to, or null/blank for the whole base.</param>
    /// <returns>The resolved scope.</returns>
    /// <exception cref="TextEditException">
    /// Thrown (as a refusal-tagged <c>InvalidArgument</c>) when the <c>cwd</c> escapes the base, is not a
    /// directory, or lands on or inside a protected directory. Every message is a fixed path-free constant.
    /// </exception>
    public EditScope Resolve(string cwd)
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
            throw NotDirectory();
        }

        if (_denylist.IsDeniedDirectory(confined.RelativePath) || IsReparentUnsafeLeaf(confined.RealPath))
        {
            throw new TextEditException(
                ErrorCodes.InvalidArgument,
                "cwd is at or inside a protected directory",
                refusalReason: ReasonDenylisted);
        }

        RootConfinement effective;
        try
        {
            effective = new RootConfinement(confined.RealPath);
        }
        catch (Exception ex) when (ex is ArgumentException or PathConfinementException)
        {
            // The resolved directory vanished between the existence check and here (a TOCTOU race); report
            // it as a clean, counted refusal rather than a generic internal error.
            throw NotDirectory();
        }

        if (!_base.ContainsPath(effective.CanonicalRoot))
        {
            throw OutsideBase();
        }

        return BuildScope(effective, confined.RelativePath);
    }

    private EditScope BuildScope(RootConfinement confinement, string scopeKey)
    {
        // The project ignore boundary is anchored at the base root (not the scoped cwd), so a cwd-scoped
        // selection still honors ancestor .gitignore/.mcpignore rules above the cwd. The prefix is the
        // effective scope's path relative to the base root ("" for the whole base).
        var prefix = scopeKey == "." ? string.Empty : scopeKey;
        var selection = new FileSelection(confinement, _denylist, _defaultIgnore, _base, prefix);
        return new EditScope(confinement, selection, scopeKey);
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

    private static TextEditException OutsideBase()
        => new(ErrorCodes.InvalidArgument, "cwd is outside the configured base root", refusalReason: ReasonOutsideBase);

    private static TextEditException NotDirectory()
        => new(ErrorCodes.InvalidArgument, "cwd is not an existing directory in the base root", refusalReason: ReasonNotDirectory);

    private static RootConfinement BuildConfinement(string path)
    {
        try
        {
            return new RootConfinement(path);
        }
        catch (Exception ex) when (ex is ArgumentException or PathConfinementException)
        {
            throw new EditStartupException($"{EnvBaseRoot} cannot be used: {ex.Message}");
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
            throw new EditStartupException($"{EnvExtraDeny} is invalid: {ex.Message}");
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
                throw new EditStartupException($"{EnvDefaultIgnore} points to a file that does not exist");
            }

            try
            {
                return IgnoreRules.Parse(File.ReadAllLines(trimmed));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new EditStartupException($"{EnvDefaultIgnore} file could not be read");
            }
        }

        return IgnoreRules.Parse(BuiltInDefaultIgnore);
    }

    /// <summary>Refuse a dangerously broad base root: a filesystem or drive root, the home directory, or one carrying a protected segment.</summary>
    private static void EnsureSafeRoot(RootConfinement baseRoot, SecretDenylist denylist)
    {
        var canonical = baseRoot.CanonicalRoot;

        if (string.IsNullOrEmpty(Path.GetDirectoryName(canonical)))
        {
            throw new EditStartupException($"{EnvBaseRoot} must not be a filesystem or drive root");
        }

        if (IsUserHome(canonical))
        {
            throw new EditStartupException($"{EnvBaseRoot} must not be the user home directory");
        }

        // Check the whole base path at once, not segment by segment, so the multi-segment marker
        // (.config/gcloud) is caught when it sits in the base's own path, matching the per-call cwd check.
        var joined = string.Join('/', canonical.Split(Separators, StringSplitOptions.RemoveEmptyEntries));
        if (!string.IsNullOrEmpty(joined) && denylist.IsDeniedDirectory(joined))
        {
            throw new EditStartupException($"{EnvBaseRoot} path must not contain a protected directory segment");
        }

        var leaf = Path.GetFileName(canonical.TrimEnd(Separators));
        foreach (var reparentUnsafe in denylist.ReparentUnsafeLeafSegments)
        {
            if (reparentUnsafe.Equals(leaf, StringComparison.OrdinalIgnoreCase))
            {
                throw new EditStartupException($"{EnvBaseRoot} must not be placed directly on a protected parent directory");
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