namespace RaccoonNinja.McpToolset.Server.FileVault.Domain;

/// <summary>
/// Shared segment-validation rules for <see cref="ProjectName"/> and <see cref="FileName"/>.
/// The charset guard rejects path separators, traversal (<c>.</c>/<c>..</c>), and anything
/// outside <c>[A-Za-z0-9._-]</c>, plus two Windows-hardening rules the Rust server lacked:
/// segments ending in a dot and reserved device names.
/// </summary>
internal static class NameValidation
{
    /// <summary>Maximum file-name length; an input cap the Rust server did not enforce.</summary>
    internal const int MaxNameLength = 128;

    /// <summary>Maximum total project-string length; an input cap the Rust server did not enforce.</summary>
    internal const int MaxProjectLength = 512;

    /// <summary>Maximum number of project segments; an input cap the Rust server did not enforce.</summary>
    internal const int MaxProjectSegments = 8;

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Returns <c>true</c> when <paramref name="segment"/> is a single, safe path segment.</summary>
    /// <param name="segment">The candidate segment.</param>
    /// <returns><c>true</c> when the segment passes every rule.</returns>
    internal static bool IsValidSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment) || segment == "." || segment == "..")
        {
            return false;
        }

        foreach (var ch in segment)
        {
            var isAllowed = ch is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '_' or '-';
            if (!isAllowed)
            {
                return false;
            }
        }

        // Win32 strips trailing dots, so `note.` and `note` would alias on disk.
        if (segment.EndsWith('.'))
        {
            return false;
        }

        // A reserved device component (`NUL`, `CON1.md`-style stems) can silently sink
        // filesystem writes on Windows, breaking the snapshot-before-commit invariant.
        var stem = segment.Split('.')[0];
        return !ReservedDeviceNames.Contains(stem);
    }
}