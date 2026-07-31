using RaccoonNinja.McpToolset.Files.Security;
using RaccoonNinja.McpToolset.Files.Selection;
using RaccoonNinja.McpToolset.Server.TextEdit.Models;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Configuration;

/// <summary>
/// The single confined root the server edits, from <c>MCP_TEXTEDIT_ROOTS</c> (a bare path or a
/// <c>name=path</c> alias). Exactly one root is required by design: edit reaches less than search, so it
/// points at one repository and a second repo means a second server instance. There are no package roots.
/// The absolute path lives only inside the confiner and is surfaced only as a basename or a hash.
/// </summary>
public sealed class RootRegistry
{
    /// <summary>The environment variable naming the single root (required, exactly one entry).</summary>
    public const string EnvRoots = "MCP_TEXTEDIT_ROOTS";

    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    private RootRegistry(string name, RootConfinement confinement, FileSelection selection)
    {
        Name = name;
        Confinement = confinement;
        Selection = selection;
    }

    /// <summary>The stable, agent-facing name for the root (alias or basename).</summary>
    public string Name { get; }

    /// <summary>The confiner bound to the root.</summary>
    public RootConfinement Confinement { get; }

    /// <summary>The selection service over the root.</summary>
    public FileSelection Selection { get; }

    /// <summary>Build the registry from the environment; fatal (via <see cref="EditStartupException"/>) on any bad config.</summary>
    /// <param name="denylist">The shared secret denylist.</param>
    /// <returns>The registry.</returns>
    /// <exception cref="EditStartupException">Thrown for a missing, multiple, unreadable, or reserved-named root.</exception>
    public static RootRegistry Load(ISecretDenylist denylist)
        => Create(denylist, Environment.GetEnvironmentVariable(EnvRoots));

    /// <summary>Build the registry from an explicit config string (the env-free path, used by tests).</summary>
    /// <param name="denylist">The shared secret denylist.</param>
    /// <param name="rawRoots">The raw roots value.</param>
    /// <returns>The registry.</returns>
    /// <exception cref="EditStartupException">Thrown for a missing, multiple, unreadable, or reserved-named root.</exception>
    internal static RootRegistry Create(ISecretDenylist denylist, string rawRoots)
    {
        ArgumentNullException.ThrowIfNull(denylist);

        var entries = string.IsNullOrWhiteSpace(rawRoots)
            ? []
            : rawRoots.Split([Path.PathSeparator, ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (entries.Length == 0)
        {
            throw new EditStartupException($"no root set; set {EnvRoots} to the single directory to edit");
        }

        if (entries.Length > 1)
        {
            throw new EditStartupException(
                $"{EnvRoots} takes exactly one root but got {entries.Length}; text-edit points at one repository, so run a second server instance for a second repo");
        }

        var (alias, path) = SplitEntry(entries[0]);
        var confinement = BuildConfinement(path);
        var name = AssignName(alias, path);
        var selection = new FileSelection(confinement, denylist);
        return new RootRegistry(name, confinement, selection);
    }

    /// <summary>The root to report from <c>describe_scope</c>, a one-entry array for shape-compatibility with the read server.</summary>
    /// <returns>The single descriptor.</returns>
    public IReadOnlyList<RootDescriptor> Describe() => [new RootDescriptor(Name, "workspace")];

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
            throw new EditStartupException($"root '{path}' cannot be used: {ex.Message}");
        }
    }

    private static string AssignName(string alias, string path)
    {
        if (!string.IsNullOrWhiteSpace(alias))
        {
            var name = alias.Trim();
            EnsureNotReserved(name);
            return name;
        }

        var basename = Path.GetFileName(path.TrimEnd(Separators));
        if (string.IsNullOrEmpty(basename))
        {
            throw new EditStartupException($"root '{path}' has no basename; give it an alias with name=path");
        }

        EnsureNotReserved(basename);
        return basename;
    }

    private static void EnsureNotReserved(string name)
    {
        if (name.StartsWith('@'))
        {
            throw new EditStartupException(
                $"root name '{name}' is reserved (names starting with '@'); give the root an explicit alias with name=path");
        }
    }
}