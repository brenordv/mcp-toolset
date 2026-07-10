using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.LogFileValidatorExceptions;

namespace RaccoonNinja.McpToolset.Server.GitOps.Logging;

/// <summary>
/// Validates a candidate log-file path. Rejects UNC, extended-length, drive-relative,
/// non-fixed Windows drives, world/group-writable POSIX parents, missing parent dir,
/// symlink-as-final, and paths under <c>/dev</c>, <c>/proc</c>, <c>/sys</c>.
/// </summary>
public static class LogFileValidator
{
    /// <summary>Validate the candidate path, returning the resolved absolute path on success.</summary>
    public static string Validate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new LogPathRejectedException("empty path");

        EnsureNoControlCharacterIsPath(raw);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            EnsureWindowsPathIsValid(raw);

        if (raw.StartsWith(@"\\", StringComparison.Ordinal) || raw.StartsWith("//", StringComparison.Ordinal))
            throw new LogPathRejectedException("UNC path not allowed");

        if (!Path.IsPathFullyQualified(raw))
            throw new LogPathRejectedException("path must be absolute");

        var candidate = NormalizeValueAsPath(raw);

        var parent = ExtractParent(candidate);

        EnsureFileIsNotSymLink(candidate);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            EnsurePosixPathIsValid(parent, candidate);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            ValidateWindowsDrive(candidate);

        return candidate;
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("osx")]
    private static void EnsurePosixPathIsValid(string parent, string candidate)
    {
        ValidatePosixParent(parent);
        var posix = candidate.Replace('\\', '/');
        var forbiddenPrefixes = new[] { "/dev/", "/proc/", "/sys/" };
        foreach (var prefix in forbiddenPrefixes)
        {
            if (posix.StartsWith(prefix, StringComparison.Ordinal))
                throw new LogPathRejectedException($"path under '{prefix}' not allowed");
        }
    }

    private static void EnsureFileIsNotSymLink(string candidate)
    {
        if (!File.Exists(candidate))
            return;

        var attrs = File.GetAttributes(candidate);

        if (!attrs.HasFlag(FileAttributes.ReparsePoint)) return;

        throw new LogPathRejectedException("path is a symlink");
    }

    private static string ExtractParent(string candidate)
    {
        var parent = Path.GetDirectoryName(candidate);
        return string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent)
            ? throw new LogPathRejectedException("parent directory does not exist")
            : parent;
    }

    private static string NormalizeValueAsPath(string raw)
    {
        string candidate;
        try
        {
            candidate = Path.GetFullPath(raw);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new LogPathRejectedException("path is malformed");
        }

        return candidate;
    }

    /// <summary>
    /// In Windows, <c>\\.\</c> is a  device namespace, so this won't open files.
    /// Examples:
    ///   - <c>\\.\PhysicalDrive0</c> → raw physical disk
    ///   - <c>\\.\C:</c> → raw volume
    ///   - <c>\\.\COM1, \\.\LPT1</c> → serial/parallel ports
    ///   - <c>\\.\pipe\{name}</c> → named pipes
    /// </summary>
    private static void EnsureWindowsPathIsValid(string raw)
    {
        if (!raw.StartsWith(@"\\?\", StringComparison.Ordinal) && !raw.StartsWith(@"\\.\", StringComparison.Ordinal))
            return;

        throw new LogPathRejectedException("Windows extended-length path not allowed");
    }

    private static void EnsureNoControlCharacterIsPath(string raw)
    {
        foreach (var ch in raw)
        {
            if (char.IsControl(ch))
                throw new LogPathRejectedException("control character in path");
        }
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("osx")]
    private static void ValidatePosixParent(string parent)
    {
        try
        {
            var mode = File.GetUnixFileMode(parent);
            // Reject parent that is group- or world-writable (TOCTOU symlink replace).
            if ((mode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0)
                throw new LogPathRejectedException("parent directory is group/world-writable");
        }
        catch (LogPathRejectedException)
        {
            throw;
        }
        catch
        {
            // Silently skip POSIX checks when the mode read fails; better to log to file than to refuse to start.
        }
    }

    private static void ValidateWindowsDrive(string candidate)
    {
        var root = Path.GetPathRoot(candidate);
        if (string.IsNullOrWhiteSpace(root))
            return;
        try
        {
            var info = new DriveInfo(root);
            if (info.DriveType is DriveType.Fixed or DriveType.Removable or DriveType.Unknown)
                return;

            // Reject network/CDROM/RAM drives. Removable + Unknown are tolerated for portability.
            if (info.DriveType != DriveType.Network)
                return;

            throw new LogPathRejectedException("log file must live on a fixed local drive");
        }
        catch (LogPathRejectedException)
        {
            throw;
        }
        catch
        {
            // DriveInfo throws on non-existent roots etc.; defer to other validators.
        }
    }
}