using RaccoonNinja.McpToolset.Files.Security;

namespace RaccoonNinja.McpToolset.Files.Selection;

/// <summary>
/// Turns a validated <see cref="FileSelector"/> into the files it names. Glob, regex, and "everything" walk
/// the tree through a <see cref="FileWalker"/> with a compiled match predicate; an explicit path list skips
/// the walk but still passes each path through the read gate, the one choke point where confinement, the
/// denylist, and the project ignore boundary are enforced before anything is opened. That symmetry is the
/// point: <c>paths: [".env", "appsettings.Production.json"]</c> must not read straight into a caller's hands
/// just because enumeration was skipped, so the gate runs on every path however it arrives. The
/// project ignore boundary (<c>.gitignore</c>, the agent-ignore files, <c>.mcpignore</c>) is evaluated through <see cref="PathIgnoreEvaluator"/>
/// anchored at the base root, so it holds even for a scoped call and cannot be reached past by
/// <c>include_ignored</c> (which only ever re-includes the built-in default tier).
/// </summary>
public sealed class FileSelection
{
    private readonly IRootResolver _root;
    private readonly ISecretDenylist _denylist;
    private readonly FileWalker _walker;
    private readonly IRootResolver _anchor;
    private readonly string _prefix;

    /// <summary>Create a selection service over a confiner and the shared denylist.</summary>
    /// <param name="root">The root confiner; selection never escapes its canonical root.</param>
    /// <param name="denylist">The non-overridable secret denylist enforced at the read gate.</param>
    /// <param name="defaultIgnore">The built-in default ignore tier passed to the walker; <c>null</c> means none.</param>
    /// <param name="anchor">The base-root confiner the project ignore boundary is evaluated against; <c>null</c> uses <paramref name="root"/> (a whole-scope call, where the walk's own root is already the base).</param>
    /// <param name="prefix">The <see cref="_root"/> path relative to <paramref name="anchor"/>, or <c>null</c>/empty for a whole-scope call; used to rebase selection-relative paths for ignore evaluation.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="root"/> or <paramref name="denylist"/> is <c>null</c>.</exception>
    public FileSelection(IRootResolver root, ISecretDenylist denylist, IgnoreRules defaultIgnore = null, IRootResolver anchor = null, string prefix = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(denylist);
        _root = root;
        _denylist = denylist;
        _walker = new FileWalker(root, denylist, defaultIgnore);
        _anchor = anchor ?? root;
        _prefix = prefix ?? string.Empty;
    }

    /// <summary>Resolve <paramref name="selector"/> to its files.</summary>
    /// <param name="selector">The validated selection request.</param>
    /// <param name="regexOptions">The guard rails for a raw-regex selector; defaults to <see cref="SafeRegexOptions"/> defaults.</param>
    /// <param name="cancellationToken">Checked periodically during the walk so a caller can bound it by time.</param>
    /// <returns>The selected files, sorted, with the skipped-symlink count and truncation flag.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="selector"/> is <c>null</c>.</exception>
    /// <exception cref="RegexCompilationException">Thrown when a raw-regex selector fails the ADR-005 guards.</exception>
    /// <exception cref="PathConfinementException">Thrown when the walk's start directory escapes the root.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the walk is cancelled mid-flight.</exception>
    public WalkResult Select(FileSelector selector, SafeRegexOptions regexOptions = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selector);

        return selector.Mode == SelectionMode.Paths
            ? SelectPaths(selector)
            : Walk(selector, regexOptions ?? new SafeRegexOptions(), cancellationToken);
    }

    private WalkResult Walk(FileSelector selector, SafeRegexOptions regexOptions, CancellationToken cancellationToken)
    {
        var match = BuildMatch(selector, regexOptions);
        var result = _walker.Walk(
            new FileWalkOptions
            {
                Start = selector.Root ?? ".",
                Match = match,
                IncludeIgnored = selector.IncludeIgnored,
                MaxResults = selector.MaxFiles,
            },
            cancellationToken);

        // The walk prunes ignored entries using rules anchored at its own (possibly scoped) root, and it can
        // re-include default-tier paths via include_ignored. Neither is authoritative for the project ignore
        // boundary: a scoped walk never consults .gitignore/.mcpignore above its root, and a re-include could
        // pull back a project-ignored file. Re-check every surfaced entry against the project tier anchored at
        // the base root, but only in the cases those gaps can arise: a re-include request or a scoped call.
        if (selector.IncludeIgnored.IsEmpty && _prefix.Length == 0)
        {
            return result;
        }

        var filtered = result.Entries.Where(entry => !IsProjectIgnored(entry.RelativePath)).ToList();
        return new WalkResult
        {
            Entries = filtered,
            SkippedSymlinks = result.SkippedSymlinks,
            Truncated = result.Truncated,
        };
    }

    private static Func<string, bool> BuildMatch(FileSelector selector, SafeRegexOptions regexOptions)
    {
        var baseMatch = BuildBaseMatch(selector, regexOptions);
        var extensionMatch = BuildExtensionMatch(selector.Extensions);
        if (baseMatch is null)
        {
            return extensionMatch;
        }

        if (extensionMatch is null)
        {
            return baseMatch;
        }

        return path => baseMatch(path) && extensionMatch(path);
    }

    private static Func<string, bool> BuildBaseMatch(FileSelector selector, SafeRegexOptions regexOptions)
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

    /// <summary>A predicate that keeps only files whose extension is in <paramref name="extensions"/>; <c>null</c> when unset.</summary>
    private static Func<string, bool> BuildExtensionMatch(IReadOnlySet<string> extensions)
    {
        if (extensions is null || extensions.Count == 0)
        {
            return null;
        }

        return path =>
        {
            var extension = Path.GetExtension(path);
            return extension.Length > 0 && extensions.Contains(extension[1..]);
        };
    }

    private WalkResult SelectPaths(FileSelector selector)
    {
        var extensionMatch = BuildExtensionMatch(selector.Extensions);
        var entries = new List<WalkEntry>();
        foreach (var raw in selector.Paths)
        {
            if (raw is not null
                && TryGate(raw, out var entry)
                && (extensionMatch is null || extensionMatch(entry.RelativePath)))
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

        if (!confined.Exists || _denylist.IsDeniedFile(confined.RelativePath) || IsProjectIgnored(confined.RelativePath))
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

    /// <summary>
    /// Whether <paramref name="relativePath"/> (relative to this selection's root) is ignored by a
    /// project ignore rule (<c>.gitignore</c>, an agent-ignore file, or <c>.mcpignore</c>), evaluated root-down from the base anchor so a scoped call
    /// still honors ancestor ignore files and no re-include can pull a project-ignored path back.
    /// </summary>
    private bool IsProjectIgnored(string relativePath)
        => PathIgnoreEvaluator.IsIgnored(_anchor.CanonicalRoot, ToBaseRelative(relativePath));

    /// <summary>Rebase a selection-relative path onto the base anchor for ignore evaluation.</summary>
    private string ToBaseRelative(string relativePath)
        => _prefix.Length == 0 ? relativePath : string.Concat(_prefix, "/", relativePath);
}