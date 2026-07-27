using RaccoonNinja.McpToolset.Files.Security;

namespace RaccoonNinja.McpToolset.Files.Selection;

/// <summary>
/// Turns a validated <see cref="FileSelector"/> into the files it names. Glob, regex, and "everything" walk
/// the tree through a <see cref="FileWalker"/> with a compiled match predicate; an explicit path list skips
/// the walk but still passes each path through the read gate, the one choke point where confinement and the
/// denylist are enforced before anything is opened. That symmetry is the point: <c>paths: [".env", "id_rsa"]</c>
/// must not read straight into a caller's hands just because enumeration was skipped, so the gate runs on
/// every path however it arrives.
/// </summary>
public sealed class FileSelection
{
    private readonly IRootResolver _root;
    private readonly ISecretDenylist _denylist;
    private readonly FileWalker _walker;

    /// <summary>Create a selection service over a confiner and the shared denylist.</summary>
    /// <param name="root">The root confiner; selection never escapes its canonical root.</param>
    /// <param name="denylist">The non-overridable secret denylist enforced at the read gate.</param>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <c>null</c>.</exception>
    public FileSelection(IRootResolver root, ISecretDenylist denylist)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(denylist);
        _root = root;
        _denylist = denylist;
        _walker = new FileWalker(root, denylist);
    }

    /// <summary>Resolve <paramref name="selector"/> to its files.</summary>
    /// <param name="selector">The validated selection request.</param>
    /// <param name="regexOptions">The guard rails for a raw-regex selector; defaults to <see cref="SafeRegexOptions"/> defaults.</param>
    /// <returns>The selected files, sorted, with the skipped-symlink count and truncation flag.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="selector"/> is <c>null</c>.</exception>
    /// <exception cref="RegexCompilationException">Thrown when a raw-regex selector fails the ADR-005 guards.</exception>
    /// <exception cref="PathConfinementException">Thrown when the walk's start directory escapes the root.</exception>
    public WalkResult Select(FileSelector selector, SafeRegexOptions regexOptions = null)
    {
        ArgumentNullException.ThrowIfNull(selector);

        return selector.Mode == SelectionMode.Paths
            ? SelectPaths(selector)
            : Walk(selector, regexOptions ?? new SafeRegexOptions());
    }

    private WalkResult Walk(FileSelector selector, SafeRegexOptions regexOptions)
    {
        var match = BuildMatch(selector, regexOptions);
        return _walker.Walk(new FileWalkOptions
        {
            Start = selector.Root ?? ".",
            Match = match,
            IncludeIgnored = selector.IncludeIgnored,
            MaxResults = selector.MaxFiles,
        });
    }

    private static Func<string, bool> BuildMatch(FileSelector selector, SafeRegexOptions regexOptions)
    {
        switch (selector.Mode)
        {
            case SelectionMode.All:
                return null;
            case SelectionMode.Glob:
                var glob = GlobCompiler.Compile(selector.Glob, selector.CaseSensitive);
                return glob.IsMatch;
            case SelectionMode.Regex:
                var compiled = SafeRegexCompiler.Compile(
                    selector.Regex,
                    regexOptions with { CaseSensitive = selector.CaseSensitive });
                return compiled.Regex.IsMatch;
            default:
                throw new SelectorException($"selection mode {selector.Mode} cannot be walked");
        }
    }

    private WalkResult SelectPaths(FileSelector selector)
    {
        var entries = new List<WalkEntry>();
        foreach (var raw in selector.Paths)
        {
            if (raw is not null && TryGate(raw, out var entry))
            {
                entries.Add(entry);
            }
        }

        entries.Sort(static (a, b) => string.CompareOrdinal(a.RelativePath, b.RelativePath));

        var truncated = false;
        if (entries.Count > selector.MaxFiles)
        {
            truncated = true;
            entries = entries.GetRange(0, selector.MaxFiles);
        }

        return new WalkResult { Entries = entries, SkippedSymlinks = 0, Truncated = truncated };
    }

    /// <summary>
    /// Run one path through the read gate. It is omitted (not surfaced) when it escapes the root, is
    /// denylisted, does not exist, or is a directory. Reporting a denied file's existence is itself recon,
    /// so refusal is silent.
    /// </summary>
    private bool TryGate(string raw, out WalkEntry entry)
    {
        entry = null;

        ConfinedPath confined;
        try
        {
            confined = _root.Confine(raw, "paths");
        }
        catch (PathConfinementException)
        {
            return false;
        }

        if (!confined.Exists || _denylist.IsDeniedFile(confined.RelativePath))
        {
            return false;
        }

        var file = new FileInfo(confined.RealPath);
        if (!file.Exists)
        {
            // Path resolved to a directory (or vanished): a file selector returns files only.
            return false;
        }

        entry = new WalkEntry
        {
            RelativePath = confined.RelativePath,
            IsDirectory = false,
            Size = file.Length,
            LastModifiedUtc = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero),
        };
        return true;
    }
}