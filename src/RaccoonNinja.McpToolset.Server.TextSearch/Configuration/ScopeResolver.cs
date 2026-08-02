using RaccoonNinja.McpToolset.Files.Security;
using RaccoonNinja.McpToolset.Files.Selection;
using RaccoonNinja.McpToolset.Server.TextSearch.Content;
using RaccoonNinja.McpToolset.Server.TextSearch.Errors;
using RaccoonNinja.McpToolset.Server.TextSearch.Logging;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Configuration;

/// <summary>
/// Resolves each call's effective scope. The base root is the hard confinement ceiling for a blank,
/// absolute, or relative <c>cwd</c>; an <c>@name[/subpath]</c> <c>cwd</c> instead resolves under a named,
/// read-only package root (an out-of-tree dependency cache registered once at startup). Every root, base
/// or package, gets the same physical-resolution confinement, the same denylist/reparent-unsafe rejection,
/// the same broad-root startup guard, and a re-bind under its own confiner after the effective confiner is
/// built, so a resolve-then-construct swap cannot escape the ceiling. No two configured roots may overlap.
/// With no package roots configured, behavior is identical to the base-root-only server.
/// </summary>
public sealed class ScopeResolver
{
    /// <summary>The environment variable naming the single base root (required).</summary>
    public const string EnvBaseRoot = "MCP_TEXTSEARCH_BASE_ROOT";

    /// <summary>The environment variable selecting or disabling the default ignore tier.</summary>
    public const string EnvDefaultIgnore = "MCP_TEXTSEARCH_DEFAULT_IGNORE";

    /// <summary>The environment variable naming additive secret-denylist patterns.</summary>
    public const string EnvExtraDeny = "MCP_TEXTSEARCH_EXTRA_DENY";

    /// <summary>The environment variable naming optional read-only package roots (dependency caches).</summary>
    public const string EnvPackageRoots = "MCP_TEXTSEARCH_PACKAGE_ROOTS";

    private const string DisableDefaultIgnore = "off";
    private const char EntrySeparator = ';';
    private const char PackagePrefix = '@';

    private const string ReasonOutsideBase = "cwd_outside_base";
    private const string ReasonOutsidePackage = "cwd_outside_package_root";
    private const string ReasonUnknownPackage = "cwd_unknown_package_root";
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
    private static readonly char[] NameBoundary = ['/', '\\'];

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private readonly RootConfinement _base;
    private readonly SecretDenylist _denylist;
    private readonly IgnoreRules _defaultIgnore;
    private readonly SearchConfig _config;
    private readonly CallScope _baseScope;
    private readonly Dictionary<string, PackageRoot> _packageRoots;
    private readonly IReadOnlyList<string> _packageRootNames;

    private ScopeResolver(
        RootConfinement baseRoot,
        SecretDenylist denylist,
        IgnoreRules defaultIgnore,
        SearchConfig config,
        List<(string Name, RootConfinement Confinement)> packageRoots)
    {
        _base = baseRoot;
        _denylist = denylist;
        _defaultIgnore = defaultIgnore;
        _config = config;
        _baseScope = BuildScope(baseRoot, ScopeKind.Base, ".");

        var map = new Dictionary<string, PackageRoot>(packageRoots.Count, StringComparer.OrdinalIgnoreCase);
        var names = new List<string>(packageRoots.Count);
        foreach (var (name, confinement) in packageRoots)
        {
            var wholeScope = BuildScope(confinement, ScopeKind.Package, $"{PackagePrefix}{name}");
            map[name] = new PackageRoot(name, confinement, wholeScope);
            names.Add(name);
        }

        _packageRoots = map;
        _packageRootNames = names;
    }

    /// <summary>The effective secret denylist (built-ins plus any operator extensions), for the scope description and DI.</summary>
    public ISecretDenylist Denylist => _denylist;

    /// <summary>The base root's basename, a human label for the scope description (never the absolute path).</summary>
    public string BaseRootName => Path.GetFileName(_base.CanonicalRoot.TrimEnd(Separators));

    /// <summary>The effective default-ignore patterns, empty when the tier is disabled.</summary>
    public IReadOnlyList<string> DefaultIgnorePatterns => _defaultIgnore.Patterns;

    /// <summary>The operator-chosen names of the configured package roots, in configuration order (never a path).</summary>
    public IReadOnlyList<string> PackageRootNames => _packageRootNames;

    /// <summary>An 8-char hash of the base canonical root, so scope logs correlate without leaking the path.</summary>
    public string RootHash => LogScrubbing.HashedValue(_base.CanonicalRoot);

    /// <summary>Build the resolver from the environment; fatal (via <see cref="SearchStartupException"/>) on bad config.</summary>
    /// <param name="config">The server config (supplies the read size cap).</param>
    /// <returns>The resolver.</returns>
    /// <exception cref="SearchStartupException">Thrown for a missing, unreadable, or dangerously broad root, an overlapping or bad-named package root, or invalid ignore/deny config.</exception>
    public static ScopeResolver Load(SearchConfig config)
        => Create(
            config,
            Environment.GetEnvironmentVariable(EnvBaseRoot),
            Environment.GetEnvironmentVariable(EnvDefaultIgnore),
            Environment.GetEnvironmentVariable(EnvExtraDeny),
            Environment.GetEnvironmentVariable(EnvPackageRoots));

    /// <summary>Build the resolver from explicit config strings (the env-free path, used by tests).</summary>
    /// <param name="config">The server config (supplies the read size cap).</param>
    /// <param name="baseRootValue">The base root path.</param>
    /// <param name="defaultIgnoreValue">The default-ignore selector: <c>off</c>, a file path, or null for the built-ins.</param>
    /// <param name="extraDenyValue">The additive deny patterns, or null.</param>
    /// <param name="packageRootsValue">The <c>;</c>-separated <c>name=path</c> (or bare path) package roots, or null.</param>
    /// <returns>The resolver.</returns>
    /// <exception cref="SearchStartupException">Thrown for a missing, unreadable, or dangerously broad root, an overlapping or bad-named package root, or invalid ignore/deny config.</exception>
    internal static ScopeResolver Create(
        SearchConfig config,
        string baseRootValue,
        string defaultIgnoreValue,
        string extraDenyValue,
        string packageRootsValue = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (string.IsNullOrWhiteSpace(baseRootValue))
        {
            throw new SearchStartupException($"no base root set; set {EnvBaseRoot} to a directory to search");
        }

        var baseRoot = BuildConfinement(baseRootValue.Trim(), EnvBaseRoot);
        var denylist = BuildDenylist(extraDenyValue);
        EnsureSafeRoot(baseRoot, denylist, EnvBaseRoot);
        var defaultIgnore = BuildDefaultIgnore(defaultIgnoreValue);
        var packageRoots = BuildPackageRoots(packageRootsValue, denylist);
        EnsureNoOverlap(baseRoot, packageRoots);
        return new ScopeResolver(baseRoot, denylist, defaultIgnore, config, packageRoots);
    }

    /// <summary>Resolve a <c>cwd</c> argument to the effective call scope, confined under the base or a package root.</summary>
    /// <param name="cwd">
    /// The absolute (or base-relative) working directory to scope to, an <c>@name[/subpath]</c> package
    /// reference, or null/blank for the whole base.
    /// </param>
    /// <returns>The resolved scope.</returns>
    /// <exception cref="TextSearchException">
    /// Thrown (as a refusal-tagged <c>InvalidArgument</c>) when the <c>cwd</c> escapes its root, is not a
    /// directory, lands on or inside a protected directory, or names an unknown package root. Every message
    /// is a fixed path-free constant.
    /// </exception>
    public CallScope Resolve(string cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd))
        {
            return _baseScope;
        }

        return cwd.StartsWith(PackagePrefix)
            ? ResolvePackage(cwd)
            : ResolveUnder(_base, cwd, ScopeKind.Base, static rel => rel);
    }

    /// <summary>Resolve an <c>@name[/subpath]</c> reference against a configured package root.</summary>
    private CallScope ResolvePackage(string cwd)
    {
        var body = cwd[1..];
        var boundary = body.IndexOfAny(NameBoundary);
        var name = boundary < 0 ? body : body[..boundary];
        var subpath = boundary < 0 ? null : body[(boundary + 1)..];

        if (name.Length == 0 || !_packageRoots.TryGetValue(name, out var package))
        {
            throw UnknownPackageRoot();
        }

        // @name, @name/, and a subpath normalizing to "." all address the whole cache and share one scope
        // identity, so two spellings never mint two cursors.
        if (string.IsNullOrWhiteSpace(subpath) || subpath.Trim() == ".")
        {
            return package.WholeScope;
        }

        return ResolveUnder(
            package.Confinement,
            subpath,
            ScopeKind.Package,
            rel => rel == "." ? $"{PackagePrefix}{package.Name}" : $"{PackagePrefix}{package.Name}/{rel}");
    }

    /// <summary>
    /// Run the shared confinement sequence under <paramref name="confiner"/>: confine, require a directory,
    /// reject a denylisted or reparent-unsafe target, build the effective confiner off the resolved real
    /// path, and re-bind it under <paramref name="confiner"/> before building the scope. The base <c>cwd</c>
    /// path and every package subpath call this, so the security boundary exists once.
    /// </summary>
    private CallScope ResolveUnder(RootConfinement confiner, string path, ScopeKind kind, Func<string, string> displayScopeKey)
    {
        ConfinedPath confined;
        try
        {
            confined = confiner.Confine(path, nameof(path));
        }
        catch (PathConfinementException)
        {
            throw Outside(kind);
        }

        if (!Directory.Exists(confined.RealPath))
        {
            throw NotDirectory();
        }

        if (_denylist.IsDeniedDirectory(confined.RelativePath) || IsReparentUnsafeLeaf(confined.RealPath))
        {
            throw new TextSearchException(
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
            // The resolved directory vanished between the existence check and here (a TOCTOU race); report it
            // as a clean, counted refusal rather than letting it surface as a generic internal error.
            throw NotDirectory();
        }

        if (!confiner.ContainsPath(effective.CanonicalRoot))
        {
            throw Outside(kind);
        }

        return BuildScope(effective, kind, displayScopeKey(confined.RelativePath));
    }

    private CallScope BuildScope(RootConfinement confinement, ScopeKind kind, string scopeKey)
    {
        var selection = new FileSelection(confinement, _denylist, _defaultIgnore);
        var reader = new GatedFileReader(confinement, _denylist, _config.MaxFileBytes);
        return new CallScope(selection, reader, kind, scopeKey);
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

    private static TextSearchException Outside(ScopeKind kind)
        => kind == ScopeKind.Base
            ? new(ErrorCodes.InvalidArgument, "cwd is outside the configured base root", refusalReason: ReasonOutsideBase)
            : new(ErrorCodes.InvalidArgument, "cwd subpath is outside its package root", refusalReason: ReasonOutsidePackage);

    private static TextSearchException UnknownPackageRoot()
        => new(ErrorCodes.InvalidArgument, "cwd names an unknown package root", refusalReason: ReasonUnknownPackage);

    private static TextSearchException NotDirectory()
        => new(ErrorCodes.InvalidArgument, "cwd is not an existing directory in the configured root", refusalReason: ReasonNotDirectory);

    private static RootConfinement BuildConfinement(string path, string envLabel)
    {
        try
        {
            return new RootConfinement(path);
        }
        catch (Exception ex) when (ex is ArgumentException or PathConfinementException)
        {
            throw new SearchStartupException($"{envLabel} cannot be used: {ex.Message}");
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

    /// <summary>Parse, confine, name, and guard each package root; fatal on any bad entry.</summary>
    private static List<(string Name, RootConfinement Confinement)> BuildPackageRoots(string raw, SecretDenylist denylist)
    {
        var result = new List<(string, RootConfinement)>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return result;
        }

        var entries = raw.Split(EntrySeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var (alias, path) = SplitEntry(entry);
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new SearchStartupException($"{EnvPackageRoots} has an entry with no path");
            }

            var confinement = BuildConfinement(path, EnvPackageRoots);
            var name = AssignName(alias, confinement);
            if (!seen.Add(name))
            {
                throw new SearchStartupException($"{EnvPackageRoots} has a duplicate package root name '{name}'");
            }

            EnsureSafeRoot(confinement, denylist, EnvPackageRoots);
            result.Add((name, confinement));
        }

        return result;
    }

    /// <summary>Split a <c>name=path</c> entry into its alias (null for a bare path) and path.</summary>
    private static (string Alias, string Path) SplitEntry(string entry)
    {
        var equals = entry.IndexOf('=', StringComparison.Ordinal);
        return equals >= 1
            ? (entry[..equals].Trim(), entry[(equals + 1)..].Trim())
            : (null, entry.Trim());
    }

    /// <summary>Determine a package root's stored name: the explicit alias, else the resolved-root basename; both validated.</summary>
    private static string AssignName(string alias, RootConfinement confinement)
    {
        if (!string.IsNullOrWhiteSpace(alias))
        {
            var name = alias.Trim();
            ValidateName(name);
            return name;
        }

        var basename = Path.GetFileName(confinement.CanonicalRoot.TrimEnd(Separators));
        if (string.IsNullOrEmpty(basename))
        {
            throw new SearchStartupException($"{EnvPackageRoots} root has no basename; give it a name with name=path");
        }

        ValidateName(basename);
        return basename;
    }

    /// <summary>Enforce the package-root name rules: non-empty, not <c>.</c>/<c>..</c>, no leading <c>@</c>, and no path separator.</summary>
    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new SearchStartupException($"{EnvPackageRoots} has an empty package root name");
        }

        // Reject control characters: a U+001E would collide with the cursor-identity field separator and a
        // NUL with the cursor delimiter, corrupting scope identity. (Operator-supplied, so defense in depth.)
        if (name.Any(char.IsControl))
        {
            throw new SearchStartupException($"{EnvPackageRoots} package root name must not contain control characters");
        }

        if (name.StartsWith(PackagePrefix))
        {
            throw new SearchStartupException($"{EnvPackageRoots} name '{name}' must not start with '@' (the '@' prefix addresses a package root)");
        }

        if (name is "." or "..")
        {
            throw new SearchStartupException($"{EnvPackageRoots} name '{name}' must not be '.' or '..'");
        }

        if (name.IndexOfAny(NameBoundary) >= 0)
        {
            throw new SearchStartupException($"{EnvPackageRoots} name '{name}' must not contain a path separator");
        }
    }

    /// <summary>Refuse any configured root that contains another (canonical, bidirectional), including an exact duplicate or a junction-smuggled overlap.</summary>
    private static void EnsureNoOverlap(RootConfinement baseRoot, List<(string Name, RootConfinement Confinement)> packageRoots)
    {
        var all = new List<RootConfinement>(packageRoots.Count + 1) { baseRoot };
        all.AddRange(packageRoots.Select(static p => p.Confinement));

        for (var i = 0; i < all.Count; i++)
        {
            for (var j = i + 1; j < all.Count; j++)
            {
                if (all[i].ContainsPath(all[j].CanonicalRoot) || all[j].ContainsPath(all[i].CanonicalRoot))
                {
                    throw new SearchStartupException(
                        $"{EnvPackageRoots} roots must not overlap the base root or each other; a cache already under the base root needs no package entry");
                }
            }
        }
    }

    /// <summary>Refuse a dangerously broad root: a filesystem or drive root, the home directory, or one carrying a protected segment.</summary>
    private static void EnsureSafeRoot(RootConfinement root, SecretDenylist denylist, string envLabel)
    {
        var canonical = root.CanonicalRoot;

        if (string.IsNullOrEmpty(Path.GetDirectoryName(canonical)))
        {
            throw new SearchStartupException($"{envLabel} must not be a filesystem or drive root");
        }

        if (IsUserHome(canonical))
        {
            throw new SearchStartupException($"{envLabel} must not be the user home directory");
        }

        // Check the whole path at once, not segment by segment, so the multi-segment marker
        // (.config/gcloud) is caught when it sits in the root's own path, matching the per-call cwd check.
        var joined = string.Join('/', canonical.Split(Separators, StringSplitOptions.RemoveEmptyEntries));
        if (!string.IsNullOrEmpty(joined) && denylist.IsDeniedDirectory(joined))
        {
            throw new SearchStartupException($"{envLabel} path must not contain a protected directory segment");
        }

        var leaf = Path.GetFileName(canonical.TrimEnd(Separators));
        foreach (var reparentUnsafe in denylist.ReparentUnsafeLeafSegments)
        {
            if (reparentUnsafe.Equals(leaf, StringComparison.OrdinalIgnoreCase))
            {
                throw new SearchStartupException($"{envLabel} must not be placed directly on a protected parent directory");
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

    /// <summary>A configured package root: its stored name, its confiner, and its prebuilt whole-cache scope.</summary>
    private sealed record PackageRoot(string Name, RootConfinement Confinement, CallScope WholeScope);
}
