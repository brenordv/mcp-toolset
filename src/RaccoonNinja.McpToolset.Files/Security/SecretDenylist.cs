using System.Text.RegularExpressions;
using RaccoonNinja.McpToolset.Files.Selection;

namespace RaccoonNinja.McpToolset.Files.Security;

/// <summary>
/// The concrete secret denylist. Its patterns are hardcoded and exposed through no configuration
/// knob, so no server and no flag can widen or disable them. Directory denials are matched against
/// any path segment (so <c>.git</c> and <c>.ssh</c> are off-limits at any depth, contents included),
/// while file denials are basename globs matched at any depth through <see cref="GlobCompiler"/>.
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
    ];

    private static readonly Regex[] FileGlobs =
        FilePatterns.Select(pattern => GlobCompiler.Compile(pattern)).ToArray();

    /// <inheritdoc />
    public bool IsDeniedFile(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var segments = relativePath.Split('/');
        return HasDeniedDirectorySegment(segments)
               || FileGlobs.Any(glob => glob.IsMatch(relativePath));
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
        var patterns = new List<string>(DeniedDirectorySegments.Count + FilePatterns.Length + 1);
        // Directory segments are denied at any depth, so the display form carries a leading **/.
        patterns.AddRange(DeniedDirectorySegments.Order(StringComparer.Ordinal).Select(segment => $"**/{segment}/**"));
        patterns.Add($"**/{GcloudParentSegment}/{GcloudSegment}/**");
        patterns.AddRange(FilePatterns);
        return patterns;
    }

    /// <summary>Whether any segment is a denied directory name, or the <c>.config/gcloud</c> pair appears consecutively.</summary>
    private static bool HasDeniedDirectorySegment(string[] segments)
    {
        for (var i = 0; i < segments.Length; i++)
        {
            if (DeniedDirectorySegments.Contains(segments[i]))
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