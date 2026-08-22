namespace RaccoonNinja.McpToolset.Common.SkillStats.Domain;

/// <summary>
/// Normalizes a Skill tool name so <c>csharp</c> and <c>/csharp</c> collapse to one key. It trims
/// surrounding whitespace, then strips every leading <c>/</c>. Case, interior slashes
/// (<c>brain/something</c>), and plugin forms (<c>plugin:skill</c>) are left untouched.
/// </summary>
public static class SkillName
{
    /// <summary>Normalize a raw skill name, reporting whether anything usable remained.</summary>
    /// <param name="raw">The raw name from the hook payload.</param>
    /// <param name="normalized">The normalized name when this returns <c>true</c>; otherwise <c>null</c>.</param>
    /// <returns><c>true</c> when a non-empty name remained after normalization.</returns>
    public static bool TryNormalize(string raw, out string normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var stripped = raw.Trim().TrimStart('/');
        if (stripped.Length == 0)
        {
            return false;
        }

        normalized = stripped;
        return true;
    }
}