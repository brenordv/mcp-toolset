using System.Text.RegularExpressions;
using RaccoonNinja.McpToolset.Files.Selection;

namespace RaccoonNinja.McpToolset.Files.Security;

/// <summary>
/// The concrete secret denylist. Its built-in patterns are hardcoded and exposed through no configuration
/// knob, so no server and no flag can widen or disable them. An operator may only <em>add</em> patterns
/// through the constructor (never remove a built-in): matching stays a boolean OR of the built-in and
/// extra sets, so the extension is additive by construction. Directory denials are matched against any
/// path segment (so <c>.git</c> and <c>.ssh</c> are off-limits at any depth, contents included), while
/// file denials are basename globs matched at any depth through <see cref="GlobCompiler"/>. Extra entries
/// are compiled through the same glob path, never through the negation-aware ignore parser, so a leading
/// <c>!</c> is a literal and cannot re-include a built-in-denied file.
/// </summary>
public sealed class SecretDenylist : ISecretDenylist
{
    private const string GcloudParentSegment = ".config";
    private const string GcloudSegment = "gcloud";

    private static readonly HashSet<string> DeniedDirectorySegments =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".hg", ".svn", ".ssh", ".aws", ".kube", ".docker", ".gnupg",
        };

    private static readonly string[] FilePatterns =
    [
        ".env*", "*.env", "*.pem", "*.key", "*.pfx", "*.p12",
        "*.p8", "*.pk8", "*.asc", "*.gpg", "*.jwk", "*.jwks",
        "id_rsa*", "id_ed25519*", "id_ecdsa*", "id_dsa*",
        "*.jks", "*.keystore", "*.pkcs12", "*.ppk",
        ".netrc", "_netrc", ".git-credentials",
        "*.tfstate", "*.tfvars", ".npmrc", ".pypirc", ".htpasswd",
        "secrets.*", "*.credentials",
        "*.settings.json",
    ];

    private static readonly Regex[] FileGlobs =
        FilePatterns.Select(pattern => GlobCompiler.Compile(pattern)).ToArray();

    // The non-final segment of every multi-segment marker; rooting a scope here would shed the parent.
    private static readonly string[] ReparentUnsafeLeaves = [GcloudParentSegment];

    private readonly HashSet<string> _extraDirectorySegments;
    private readonly string[] _extraFilePatterns;
    private readonly Regex[] _extraFileGlobs;

    /// <summary>Create the denylist, optionally adding operator-supplied patterns to the built-in set.</summary>
    /// <param name="extraPatterns">
    /// Additive patterns: an entry ending <c>/</c> is a bare directory segment denied at any depth;
    /// otherwise it is a file basename glob. <c>null</c> or empty adds nothing.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when an entry is an absolute path or a directory entry is not a single bare segment.</exception>
    /// <exception cref="Selection.RegexCompilationException">Thrown when a file-glob entry is malformed.</exception>
    public SecretDenylist(IReadOnlyList<string> extraPatterns = null)
    {
        var extraDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var extraFilePatterns = new List<string>();
        var extraFileGlobs = new List<Regex>();

        if (extraPatterns is not null)
        {
            foreach (var raw in extraPatterns)
            {
                AddExtraPattern(raw, extraDirectories, extraFilePatterns, extraFileGlobs);
            }
        }

        _extraDirectorySegments = extraDirectories;
        _extraFilePatterns = extraFilePatterns.ToArray();
        _extraFileGlobs = extraFileGlobs.ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> ReparentUnsafeLeafSegments => ReparentUnsafeLeaves;

    /// <inheritdoc />
    public bool IsDeniedFile(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var segments = relativePath.Split('/');
        return HasDeniedDirectorySegment(segments)
               || FileGlobs.Any(glob => glob.IsMatch(relativePath))
               || _extraFileGlobs.Any(glob => glob.IsMatch(relativePath));
    }

    /// <inheritdoc />
    public bool IsDeniedDirectory(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return HasDeniedDirectorySegment(relativePath.Split('/'));
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> DescribePatterns()
    {
        var patterns = new List<string>(
            DeniedDirectorySegments.Count + FilePatterns.Length + _extraDirectorySegments.Count + _extraFilePatterns.Length + 1);
        // Directory segments are denied at any depth, so the display form carries a leading **/.
        patterns.AddRange(DeniedDirectorySegments.Order(StringComparer.Ordinal).Select(segment => $"**/{segment}/**"));
        patterns.Add($"**/{GcloudParentSegment}/{GcloudSegment}/**");
        patterns.AddRange(FilePatterns);
        patterns.AddRange(_extraDirectorySegments.Order(StringComparer.Ordinal).Select(segment => $"**/{segment}/**"));
        patterns.AddRange(_extraFilePatterns);
        return patterns;
    }

    /// <summary>Classify and add one extra pattern to the directory-segment or file-glob set.</summary>
    private static void AddExtraPattern(
        string raw,
        HashSet<string> extraDirectories,
        List<string> extraFilePatterns,
        List<Regex> extraFileGlobs)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        var pattern = raw.Trim();
        if (LooksAbsolute(pattern))
        {
            throw new ArgumentException($"extra denylist entry '{pattern}' must not be an absolute path", nameof(raw));
        }

        if (pattern.EndsWith('/'))
        {
            var segment = pattern[..^1];
            if (segment.Length == 0 || segment.Contains('/') || segment.Contains('\\'))
            {
                throw new ArgumentException($"extra denylist directory entry '{pattern}' must be a single bare segment", nameof(raw));
            }

            extraDirectories.Add(segment);
            return;
        }

        extraFilePatterns.Add(pattern);
        extraFileGlobs.Add(GlobCompiler.Compile(pattern));
    }

    /// <summary>Whether a pattern looks like an absolute path (rooted or drive-qualified), which is never a valid entry.</summary>
    private static bool LooksAbsolute(string pattern)
        => pattern.StartsWith('/')
           || pattern.StartsWith('\\')
           || (pattern.Length >= 2 && pattern[1] == ':');

    /// <summary>Whether any segment is a denied directory name, or the <c>.config/gcloud</c> pair appears consecutively.</summary>
    private bool HasDeniedDirectorySegment(string[] segments)
    {
        for (var i = 0; i < segments.Length; i++)
        {
            if (DeniedDirectorySegments.Contains(segments[i]) || _extraDirectorySegments.Contains(segments[i]))
            {
                return true;
            }

            if (i + 1 < segments.Length
                && segments[i].Equals(GcloudParentSegment, StringComparison.OrdinalIgnoreCase)
                && segments[i + 1].Equals(GcloudSegment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}