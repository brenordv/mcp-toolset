using RaccoonNinja.McpToolset.Files.Security;

namespace RaccoonNinja.McpToolset.Files.Selection;

/// <summary>
/// Walks a confined root and returns the entries that survive four independent prunings: the root
/// confinement (the walk only ever enumerates real directories inside the canonical root), the
/// non-overridable secret denylist, the ignore-file rules, and symlink skipping. It never descends a
/// reparse-point (symbolic-link or junction) directory, which both keeps the walk inside the real tree
/// and makes a symlink cycle impossible, and it counts every skipped link so the caller can report an
/// aggregate rather than leave the entries looking mysteriously absent. Confinement and the denylist are
/// the security controls; ignore is a convenience rail the caller can disable. The result is sorted
/// ordinal by root-relative path so pagination is deterministic.
/// </summary>
public sealed class FileWalker
{
    private readonly IRootResolver _root;
    private readonly ISecretDenylist _denylist;

    /// <summary>Create a walker bound to a confiner and the shared denylist.</summary>
    /// <param name="root">The root confiner; the walk stays inside its canonical root.</param>
    /// <param name="denylist">The non-overridable secret denylist applied to every entry.</param>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <c>null</c>.</exception>
    public FileWalker(IRootResolver root, ISecretDenylist denylist)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(denylist);
        _root = root;
        _denylist = denylist;
    }

    /// <summary>Walk the tree under <paramref name="options"/> and return the surviving, sorted entries.</summary>
    /// <param name="options">The walk configuration.</param>
    /// <returns>The matched entries plus the skipped-symlink count and the truncation flag.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Thrown when the start path is not an existing directory inside the root.</exception>
    /// <exception cref="PathConfinementException">Thrown when the start path escapes the root.</exception>
    public WalkResult Walk(FileWalkOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var start = _root.Confine(options.Start ?? ".", "start");
        if (!start.Exists || !Directory.Exists(start.RealPath))
        {
            throw new ArgumentException($"start '{options.Start}' is not an existing directory", nameof(options));
        }

        var startRel = start.RelativePath == "." ? string.Empty : start.RelativePath;
        var results = new List<WalkEntry>();
        var skippedSymlinks = 0;
        var visited = 0;
        var capHit = false;

        var stack = new Stack<Frame>();
        stack.Push(new Frame(start.RealPath, startRel, BuildInitialRules(startRel)));

        while (stack.Count > 0 && !capHit)
        {
            var frame = stack.Pop();
            foreach (var info in EnumerateOrdered(frame.RealPath))
            {
                if (++visited > options.MaxVisitedNodes)
                {
                    capHit = true;
                    break;
                }

                Process(info, frame, options, stack, results, ref skippedSymlinks);
            }
        }

        results.Sort(static (a, b) => string.CompareOrdinal(a.RelativePath, b.RelativePath));

        var truncated = capHit;
        if (results.Count > options.MaxResults)
        {
            truncated = true;
            results = results.GetRange(0, options.MaxResults);
        }

        return new WalkResult
        {
            Entries = results,
            SkippedSymlinks = skippedSymlinks,
            Truncated = truncated,
        };
    }

    /// <summary>Prune, emit, or descend a single entry according to the four controls.</summary>
    private void Process(
        FileSystemInfo info,
        Frame frame,
        FileWalkOptions options,
        Stack<Frame> stack,
        List<WalkEntry> results,
        ref int skippedSymlinks)
    {
        var isDirectory = (info.Attributes & FileAttributes.Directory) != 0;
        var relativePath = frame.Rel.Length == 0 ? info.Name : string.Concat(frame.Rel, "/", info.Name);

        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            // A symbolic link or junction: never descended, never read. Counted so it isn't a silent gap.
            skippedSymlinks++;
            return;
        }

        var denied = isDirectory
            ? _denylist.IsDeniedDirectory(relativePath)
            : _denylist.IsDeniedFile(relativePath);
        if (denied)
        {
            return;
        }

        if (!options.IncludeIgnored && frame.Rules.IsIgnored(relativePath, isDirectory))
        {
            return;
        }

        if (isDirectory)
        {
            if (options.IncludeDirectories && Matches(options.Match, relativePath))
            {
                results.Add(ToEntry(info, relativePath, isDirectory: true));
            }

            var childRules = IgnoreRules.Combine([frame.Rules, IgnoreRules.Load(info.FullName, relativePath)]);
            stack.Push(new Frame(info.FullName, relativePath, childRules));
            return;
        }

        if (Matches(options.Match, relativePath))
        {
            results.Add(ToEntry(info, relativePath, isDirectory: false));
        }
    }

    /// <summary>Enumerate a directory's children in ordinal name order, tolerating a mid-walk removal.</summary>
    private static FileSystemInfo[] EnumerateOrdered(string directory)
    {
        FileSystemInfo[] children;
        try
        {
            children = new DirectoryInfo(directory).GetFileSystemInfos();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }

        Array.Sort(children, static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        return children;
    }

    private static bool Matches(Func<string, bool> match, string relativePath)
        => match is null || match(relativePath);

    private static WalkEntry ToEntry(FileSystemInfo info, string relativePath, bool isDirectory)
        => new()
        {
            RelativePath = relativePath,
            IsDirectory = isDirectory,
            Size = isDirectory || info is not FileInfo file ? 0 : file.Length,
            LastModifiedUtc = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
        };

    /// <summary>Accumulate the ignore rules that apply at the start directory, from the root down through it.</summary>
    private IgnoreRules BuildInitialRules(string startRel)
    {
        var sets = new List<IgnoreRules> { IgnoreRules.Load(_root.CanonicalRoot, string.Empty) };
        if (startRel.Length == 0)
        {
            return IgnoreRules.Combine(sets);
        }

        var relSoFar = string.Empty;
        var realSoFar = _root.CanonicalRoot;
        foreach (var segment in startRel.Split('/'))
        {
            relSoFar = relSoFar.Length == 0 ? segment : string.Concat(relSoFar, "/", segment);
            realSoFar = Path.Combine(realSoFar, segment);
            sets.Add(IgnoreRules.Load(realSoFar, relSoFar));
        }

        return IgnoreRules.Combine(sets);
    }

    /// <summary>One directory awaiting its turn on the walk stack, carrying the ignore rules in force there.</summary>
    private sealed record Frame(string RealPath, string Rel, IgnoreRules Rules);
}