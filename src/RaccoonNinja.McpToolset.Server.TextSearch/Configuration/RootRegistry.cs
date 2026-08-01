using RaccoonNinja.McpToolset.Files.Security;
using RaccoonNinja.McpToolset.Files.Selection;
using RaccoonNinja.McpToolset.Server.TextSearch.Content;
using RaccoonNinja.McpToolset.Server.TextSearch.Errors;
using RaccoonNinja.McpToolset.Server.TextSearch.Models;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Configuration;

/// <summary>
/// The set of named, confined roots the server searches. Workspace roots come from
/// <c>MCP_TEXTSEARCH_ROOTS</c>, package roots from <c>MCP_TEXTSEARCH_PACKAGE_ROOTS</c>; each entry is a
/// path or a <c>name=path</c> alias. Names are globally unique, never start with the reserved <c>@</c>,
/// and no root may contain another (that would let a nested cache be swept without the package-search
/// guard). A tool's <c>root</c> argument resolves through <see cref="Resolve"/> to an ordered target set.
/// </summary>
public sealed class RootRegistry
{
    /// <summary>The environment variable naming the workspace roots (required).</summary>
    public const string EnvWorkspaceRoots = "MCP_TEXTSEARCH_ROOTS";

    /// <summary>The environment variable naming the opt-in package roots.</summary>
    public const string EnvPackageRoots = "MCP_TEXTSEARCH_PACKAGE_ROOTS";

    /// <summary>The reserved target selecting every package root.</summary>
    public const string PackagesTarget = "@packages";

    /// <summary>The reserved target selecting every root.</summary>
    public const string AllTarget = "@all";

    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    private readonly IReadOnlyList<RootSpec> _roots;
    private readonly IReadOnlyList<RootSpec> _workspace;
    private readonly IReadOnlyList<RootSpec> _package;
    private readonly Dictionary<string, RootSpec> _byName;

    private RootRegistry(IReadOnlyList<RootSpec> roots)
    {
        _roots = roots;
        _workspace = roots.Where(root => root.Kind == RootKind.Workspace).ToArray();
        _package = roots.Where(root => root.Kind == RootKind.Package).ToArray();
        _byName = roots.ToDictionary(root => root.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Every configured root, ordered by name.</summary>
    public IReadOnlyList<RootSpec> All => _roots;

    /// <summary>Build the registry from the environment; fatal (via <see cref="SearchStartupException"/>) on any bad config.</summary>
    /// <param name="config">The server config (supplies the read size cap).</param>
    /// <param name="denylist">The shared secret denylist.</param>
    /// <returns>The registry.</returns>
    /// <exception cref="SearchStartupException">Thrown for a missing/unreadable/duplicate/reserved/overlapping root.</exception>
    public static RootRegistry Load(SearchConfig config, ISecretDenylist denylist)
        => Create(
            config,
            denylist,
            Environment.GetEnvironmentVariable(EnvWorkspaceRoots),
            Environment.GetEnvironmentVariable(EnvPackageRoots));

    /// <summary>Build the registry from explicit config strings (the env-free path, used by tests).</summary>
    /// <param name="config">The server config (supplies the read size cap).</param>
    /// <param name="denylist">The shared secret denylist.</param>
    /// <param name="workspaceRoots">The raw workspace-roots list.</param>
    /// <param name="packageRoots">The raw package-roots list, or null.</param>
    /// <returns>The registry.</returns>
    /// <exception cref="SearchStartupException">Thrown for a missing/unreadable/duplicate/reserved/overlapping root.</exception>
    internal static RootRegistry Create(SearchConfig config, ISecretDenylist denylist, string workspaceRoots, string packageRoots)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(denylist);

        var specs = new List<RootSpec>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ParseCategory(workspaceRoots, RootKind.Workspace, config, denylist, specs, names);
        if (specs.Count == 0)
        {
            throw new SearchStartupException($"no workspace root set; set {EnvWorkspaceRoots} to a directory to search");
        }

        ParseCategory(packageRoots, RootKind.Package, config, denylist, specs, names);

        EnsureNoOverlap(specs);
        specs.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        return new RootRegistry(specs);
    }

    /// <summary>Resolve a <c>root</c> argument to the ordered target roots.</summary>
    /// <param name="target">A root name, <c>@packages</c>, <c>@all</c>, or null/blank (all workspace roots).</param>
    /// <returns>The ordered target roots.</returns>
    /// <exception cref="TextSearchException">Thrown (as <c>InvalidArgument</c>) when the name is unknown.</exception>
    public IReadOnlyList<RootSpec> Resolve(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return _workspace;
        }

        if (target.Equals(AllTarget, StringComparison.OrdinalIgnoreCase))
        {
            return _roots;
        }

        if (target.Equals(PackagesTarget, StringComparison.OrdinalIgnoreCase))
        {
            return _package;
        }

        return _byName.TryGetValue(target, out var spec)
            ? [spec]
            : throw TextSearchException.InvalidArgument($"unknown root '{target}'");
    }

    /// <summary>The roots to report from <c>describe_scope</c>, name and kind only.</summary>
    /// <returns>The descriptors.</returns>
    public IReadOnlyList<RootDescriptor> Describe()
        =>
        [
            .. _roots.Select(root =>
                new RootDescriptor(root.Name, root.Kind == RootKind.Workspace ? "workspace" : "package"))
        ];

    private static void ParseCategory(
        string raw,
        RootKind kind,
        SearchConfig config,
        ISecretDenylist denylist,
        List<RootSpec> specs,
        HashSet<string> names)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        foreach (var entry in raw.Split([Path.PathSeparator, ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var (alias, path) = SplitEntry(entry);
            var confinement = BuildConfinement(path);
            var name = AssignName(alias, path, names);
            var selection = new FileSelection(confinement, denylist);
            var reader = new GatedFileReader(confinement, denylist, config.MaxFileBytes);
            specs.Add(new RootSpec(name, kind, confinement, selection, reader));
        }
    }

    private static (string Alias, string Path) SplitEntry(string entry)
    {
        var equals = entry.IndexOf('=', StringComparison.Ordinal);
        return equals >= 1
            ? (entry[..equals].Trim(), entry[(equals + 1)..].Trim())
            : (null, entry);
    }

    private static RootConfinement BuildConfinement(string path)
    {
        try
        {
            return new RootConfinement(path);
        }
        catch (Exception ex) when (ex is ArgumentException or PathConfinementException)
        {
            throw new SearchStartupException($"root '{path}' cannot be used: {ex.Message}");
        }
    }

    private static string AssignName(string alias, string path, HashSet<string> names)
    {
        if (!string.IsNullOrWhiteSpace(alias))
        {
            var name = alias.Trim();
            EnsureNotReserved(name);
            return names.Add(name)
                ? name
                : throw new SearchStartupException($"duplicate root name '{name}'");
        }

        var basename = Path.GetFileName(path.TrimEnd(Separators));
        if (string.IsNullOrEmpty(basename))
        {
            throw new SearchStartupException($"root '{path}' has no basename; give it an alias with name=path");
        }

        EnsureNotReserved(basename);

        var unique = basename;
        var suffix = 2;
        while (!names.Add(unique))
        {
            unique = $"{basename}-{suffix++}";
        }

        return unique;
    }

    private static void EnsureNotReserved(string name)
    {
        if (name.StartsWith('@'))
        {
            throw new SearchStartupException(
                $"root name '{name}' is reserved (names starting with '@'); give the root an explicit alias with name=path");
        }
    }

    private static void EnsureNoOverlap(List<RootSpec> specs)
    {
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        for (var i = 0; i < specs.Count; i++)
        {
            for (var j = i + 1; j < specs.Count; j++)
            {
                var a = specs[i].Confinement.CanonicalRoot;
                var b = specs[j].Confinement.CanonicalRoot;
                if (IsSameOrNested(a, b, comparison) || IsSameOrNested(b, a, comparison))
                {
                    throw new SearchStartupException(
                        $"roots '{specs[i].Name}' and '{specs[j].Name}' overlap; a root must not contain another root");
                }
            }
        }
    }

    private static bool IsSameOrNested(string outer, string inner, StringComparison comparison)
        => inner.Equals(outer, comparison)
           || inner.StartsWith(outer + Path.DirectorySeparatorChar, comparison)
           || inner.StartsWith(outer + Path.AltDirectorySeparatorChar, comparison);
}