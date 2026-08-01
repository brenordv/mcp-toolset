using System.Runtime.InteropServices;

namespace RaccoonNinja.McpToolset.Files.Security;

/// <summary>
/// Confines paths to a single root through a real, filesystem-aware resolution rather than a lexical
/// prefix check. It keeps the syntactic pre-filter seeded from git-ops's confinement (UNC,
/// extended-length, device, drive-relative, and NTFS alternate-data-stream forms are all refused up
/// front) and adds the guard neither existing server had: every component of the path is resolved
/// through its symbolic links and junctions, root-downward, so an intermediate junction cannot smuggle
/// the target outside the root. The root itself is canonicalized once at construction, so a symlinked
/// root still passes its own containment check, and comparison follows the host filesystem's case rules.
/// </summary>
public sealed class RootConfinement : IRootResolver
{
    private const int MaxSymlinkHops = 40;

    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>Create a confiner bound to <paramref name="root"/>, canonicalizing it once.</summary>
    /// <param name="root">The absolute root directory that every confined path must stay inside.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="root"/> is null, blank, or does not exist as a directory.</exception>
    public RootConfinement(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var lexicalRoot = Path.GetFullPath(root);
        if (!Directory.Exists(lexicalRoot))
        {
            throw new ArgumentException($"root '{root}' is not an existing directory", nameof(root));
        }

        CanonicalRoot = Canonicalize(lexicalRoot, nameof(root)).TrimEnd(Separators);
    }

    /// <inheritdoc />
    public string CanonicalRoot { get; }

    /// <inheritdoc />
    public ConfinedPath Confine(string candidate, string paramName = "path")
    {
        ArgumentNullException.ThrowIfNull(candidate);

        RejectHostileSyntax(candidate, paramName);

        var lexical = ResolveLexical(candidate, paramName);
        var real = Canonicalize(lexical, paramName);
        EnsureWithinRoot(real, paramName);

        return new ConfinedPath
        {
            RealPath = real,
            RelativePath = ToRepoRelativePosix(real),
            Exists = Path.Exists(real),
        };
    }

    /// <summary>
    /// Whether <paramref name="absolutePath"/> resolves to the root itself or a path beneath it, with every
    /// symbolic link in the path collapsed the same way the root was canonicalized (so a <c>/var</c> path on
    /// macOS is compared as its <c>/private/var</c> real path). A not-yet-created leaf is resolved against its
    /// longest existing ancestor. Returns <c>false</c> when the path cannot be resolved.
    /// </summary>
    /// <param name="absolutePath">An absolute path to test against the root.</param>
    /// <returns><c>true</c> when the resolved path is the root or lies inside it.</returns>
    public bool ContainsPath(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        string real;
        try
        {
            real = Canonicalize(Path.GetFullPath(absolutePath), nameof(absolutePath));
        }
        catch (Exception ex) when (ex is PathConfinementException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        var normalized = real.TrimEnd(Separators);
        return normalized.Equals(CanonicalRoot, PathComparison)
            || normalized.StartsWith(CanonicalRoot + Path.DirectorySeparatorChar, PathComparison)
            || normalized.StartsWith(CanonicalRoot + Path.AltDirectorySeparatorChar, PathComparison);
    }

    /// <summary>Refuse the syntactic forms that defeat a lexical prefix check before any resolution runs.</summary>
    private static void RejectHostileSyntax(string raw, string paramName)
    {
        if (raw.Contains('\0'))
        {
            throw new PathConfinementException(paramName, "contains a NUL character");
        }

        // Order matters: a UNC prefix (\\ or //) is caught before the extended-length form, which also begins with \\.
        if (raw.StartsWith(@"\\", StringComparison.Ordinal) || raw.StartsWith("//", StringComparison.Ordinal))
        {
            throw new PathConfinementException(paramName, "uses a UNC prefix");
        }

        if (raw.StartsWith(@"\\?\", StringComparison.Ordinal) || raw.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            throw new PathConfinementException(paramName, "uses an extended-length prefix");
        }

        if (raw.Length >= 2 && raw[1] == ':' && (raw.Length == 2 || (raw[2] != '\\' && raw[2] != '/')))
        {
            throw new PathConfinementException(paramName, "is drive-relative");
        }

        RejectAlternateDataStream(raw, paramName);
    }

    /// <summary>On Windows, refuse an NTFS alternate-data-stream suffix (a <c>:</c> past any drive letter).</summary>
    private static void RejectAlternateDataStream(string raw, string paramName)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var tail = raw.Length >= 2 && raw[1] == ':' ? raw[2..] : raw;
        if (tail.Contains(':'))
        {
            throw new PathConfinementException(paramName, "contains an alternate data stream");
        }
    }

    /// <summary>Combine the candidate with the root and fold <c>.</c>/<c>..</c> lexically, rejecting malformed input.</summary>
    private string ResolveLexical(string candidate, string paramName)
    {
        try
        {
            return Path.GetFullPath(Path.Combine(CanonicalRoot, candidate));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new PathConfinementException(paramName, "is malformed");
        }
    }

    /// <summary>
    /// Resolve <paramref name="lexicalAbsolute"/> to its real absolute path by collapsing every reparse
    /// point in the chain. Only the longest existing ancestor is resolved physically; a not-yet-created
    /// leaf is appended to its resolved parent so create/replace-of-missing still confines correctly.
    /// </summary>
    private static string Canonicalize(string lexicalAbsolute, string paramName)
    {
        var (existing, missing) = SplitAtLongestExisting(lexicalAbsolute);
        if (existing is null)
        {
            throw new PathConfinementException(paramName, "cannot be resolved");
        }

        var realExisting = OperatingSystem.IsWindows()
            ? Win32Path.GetFinalPath(existing)
            : ResolveLinksBySegment(existing, paramName);

        return missing.Length == 0
            ? realExisting
            : Path.GetFullPath(Path.Combine(realExisting, missing));
    }

    /// <summary>Split a lexical path into its longest existing ancestor and the not-yet-created remainder.</summary>
    private static (string Existing, string Missing) SplitAtLongestExisting(string lexicalAbsolute)
    {
        if (Path.Exists(lexicalAbsolute))
        {
            return (lexicalAbsolute, string.Empty);
        }

        var missing = new List<string>();
        var current = lexicalAbsolute;
        while (true)
        {
            var parent = Path.GetDirectoryName(current);
            if (parent is null)
            {
                return (null, lexicalAbsolute);
            }

            missing.Add(Path.GetFileName(current));
            current = parent;
            if (Path.Exists(current))
            {
                missing.Reverse();
                return (current, string.Join(Path.DirectorySeparatorChar, missing));
            }
        }
    }

    /// <summary>
    /// Resolve reparse points one component at a time (the managed path for Unix, which has no
    /// <c>realpath</c> binding). Re-scans from the top after each substitution so an intermediate link is
    /// followed, and caps total hops so a symlink cycle terminates instead of looping.
    /// </summary>
    private static string ResolveLinksBySegment(string existingAbsolute, string paramName)
    {
        var path = existingAbsolute;
        for (var hops = 0; hops <= MaxSymlinkHops; hops++)
        {
            if (!TryFindFirstLink(path, out var linkParent, out var rawTarget, out var tail))
            {
                return path;
            }

            var targetAbsolute = Path.IsPathRooted(rawTarget)
                ? rawTarget
                : Path.Combine(linkParent, rawTarget);

            var rebuilt = Path.GetFullPath(tail.Length == 0
                ? targetAbsolute
                : Path.Combine(targetAbsolute, tail));

            if (!Path.Exists(rebuilt))
            {
                // A dangling link; nothing further to resolve, fall back to the lexical rebuild.
                return rebuilt;
            }

            path = rebuilt;
        }

        throw new PathConfinementException(paramName, "resolves through too many symbolic links");
    }

    /// <summary>Find the first reparse-point component of <paramref name="path"/> and report its one-hop target and the trailing segments.</summary>
    private static bool TryFindFirstLink(string path, out string linkParent, out string rawTarget, out string tail)
    {
        linkParent = null;
        rawTarget = null;
        tail = null;

        var anchor = Path.GetPathRoot(path) ?? string.Empty;
        var names = path[anchor.Length..].Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        var current = anchor;

        for (var i = 0; i < names.Length; i++)
        {
            var component = Path.Combine(current, names[i]);
            var attributes = File.GetAttributes(component);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                FileSystemInfo info = attributes.HasFlag(FileAttributes.Directory)
                    ? new DirectoryInfo(component)
                    : new FileInfo(component);
                linkParent = current;
                rawTarget = info.LinkTarget;
                tail = string.Join(Path.DirectorySeparatorChar, names.Skip(i + 1));
                return true;
            }

            current = component;
        }

        return false;
    }

    /// <summary>Verify <paramref name="candidateReal"/> is the root itself or sits beneath it; otherwise refuse.</summary>
    private void EnsureWithinRoot(string candidateReal, string paramName)
    {
        var normalized = candidateReal.TrimEnd(Separators);
        if (normalized.Equals(CanonicalRoot, PathComparison)
            || normalized.StartsWith(CanonicalRoot + Path.DirectorySeparatorChar, PathComparison)
            || normalized.StartsWith(CanonicalRoot + Path.AltDirectorySeparatorChar, PathComparison))
        {
            return;
        }

        throw new PathConfinementException(paramName, "escapes the repository root");
    }

    /// <summary>Render the confined absolute path as a <c>/</c>-separated root-relative path (<c>.</c> for the root itself).</summary>
    private string ToRepoRelativePosix(string candidateReal)
    {
        var relative = Path.GetRelativePath(CanonicalRoot, candidateReal)
            .Replace(Path.DirectorySeparatorChar, '/');
        return string.IsNullOrWhiteSpace(relative) || relative == "." ? "." : relative;
    }
}