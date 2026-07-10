using System.Runtime.InteropServices;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;

namespace RaccoonNinja.McpToolset.Server.GitOps.Security;

/// <summary>
/// Confines an AI-supplied path to the resolved repository root and returns a
/// repo-relative POSIX-style path suitable for positional use after <c>--</c>.
/// </summary>
public static class PathConfinement
{
    /// <summary>Confine <paramref name="raw"/> under <paramref name="repoRoot"/>.</summary>
    public static string Confine(string repoRoot, string raw, string paramName = "path")
    {
        ArgumentValidation.RejectIfUnsafeValue(paramName, raw);

        // Syntactic rejections, in order: a UNC prefix (\\ or //) is caught before the
        // extended-length form because the latter also begins with \\.
        RejectUncPrefix(raw, paramName);
        RejectExtendedLengthPrefix(raw, paramName);
        RejectDriveRelative(raw, paramName);
        RejectAlternateDataStream(raw, paramName);

        var rootResolved = Path.GetFullPath(repoRoot);
        var candidate = ResolveCandidate(rootResolved, raw, paramName);
        EnsureWithinRoot(rootResolved, candidate, paramName);

        return ToRepoRelativePosix(rootResolved, candidate);
    }

    /// <summary>Reject a UNC prefix (<c>\\server\share</c> or <c>//server/share</c>).</summary>
    private static void RejectUncPrefix(string raw, string paramName)
    {
        if (raw.StartsWith(@"\\", StringComparison.Ordinal) || raw.StartsWith("//", StringComparison.Ordinal))
            throw OutsideRepo(paramName, "uses a UNC prefix");
    }

    /// <summary>Reject a Win32 extended-length / device prefix (<c>\\?\</c> or <c>\\.\</c>).</summary>
    private static void RejectExtendedLengthPrefix(string raw, string paramName)
    {
        if (raw.StartsWith(@"\\?\", StringComparison.Ordinal) || raw.StartsWith(@"\\.\", StringComparison.Ordinal))
            throw OutsideRepo(paramName, "uses an extended-length prefix");
    }

    /// <summary>Reject a drive-relative path (<c>C:foo</c>) that has a drive letter but no rooting separator.</summary>
    private static void RejectDriveRelative(string raw, string paramName)
    {
        if (raw.Length >= 2 && raw[1] == ':' && (raw.Length == 2 || (raw[2] != '\\' && raw[2] != '/')))
            throw OutsideRepo(paramName, "is drive-relative");
    }

    /// <summary>On Windows, reject an NTFS alternate-data-stream suffix (a <c>:</c> past any drive letter).</summary>
    private static void RejectAlternateDataStream(string raw, string paramName)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var tail = raw.Length >= 2 && raw[1] == ':' ? raw[2..] : raw;
        if (tail.Contains(':'))
            throw OutsideRepo(paramName, "contains an alternate data stream");
    }

    /// <summary>Resolve <paramref name="raw"/> against the root into an absolute path, rejecting malformed input.</summary>
    private static string ResolveCandidate(string rootResolved, string raw, string paramName)
    {
        try
        {
            return Path.GetFullPath(Path.Combine(rootResolved, raw));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw OutsideRepo(paramName, "is malformed");
        }
    }

    /// <summary>Verify <paramref name="candidate"/> is the root itself or sits beneath it; otherwise reject.</summary>
    private static void EnsureWithinRoot(string rootResolved, string candidate, string paramName)
    {
        var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var normalizedRoot = rootResolved.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedCandidate = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!normalizedCandidate.Equals(normalizedRoot, comparison)
            && !normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison)
            && !normalizedCandidate.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, comparison))
        {
            throw OutsideRepo(paramName, "escapes the repository root");
        }
    }

    /// <summary>Render the confined absolute path as a repo-relative POSIX path (<c>.</c> for the root itself).</summary>
    private static string ToRepoRelativePosix(string rootResolved, string candidate)
    {
        var relative = Path.GetRelativePath(rootResolved, candidate).Replace(Path.DirectorySeparatorChar, '/');
        return string.IsNullOrWhiteSpace(relative) ? "." : relative;
    }

    /// <summary>Build a <see cref="PathOutsideRepoException"/> with the standard <c>path under '{param}' {reason}</c> message.</summary>
    private static PathOutsideRepoException OutsideRepo(string paramName, string reason)
        => new($"path under '{paramName}' {reason}",
            new Dictionary<string, object> { ["param"] = paramName });
}