namespace RaccoonNinja.McpToolset.Files.Security;

/// <summary>The result of scanning a file's content for a secret shape.</summary>
/// <param name="IsSecret">Whether the content matched a detector (or a detector timed out, failing closed).</param>
/// <param name="DetectorId">The id of the detector that matched, or <c>null</c> when none did.</param>
public readonly record struct SecretScanResult(bool IsSecret, string DetectorId)
{
    /// <summary>A clean result: no detector matched.</summary>
    public static SecretScanResult None { get; } = new(false, null);
}

/// <summary>
/// Scans decoded file content for well-known secret shapes (cloud keys, private keys, tokens) so a file
/// whose content looks secret is withheld regardless of its name or ignore status. It is a probabilistic
/// layer over the name-based <see cref="ISecretDenylist"/>: it catches secrets in otherwise-legitimate,
/// committed files, but cannot prove a file is clean (a low-entropy, encoded, or split secret can slip).
/// </summary>
public interface ISecretContentScanner
{
    /// <summary>The ids of the active detectors, for scope disclosure (names only, never a matched value).</summary>
    IReadOnlyList<string> DetectorIds { get; }

    /// <summary>Scan <paramref name="content"/> and report whether it matches a secret detector.</summary>
    /// <param name="content">The raw file bytes; binary content is not scanned.</param>
    /// <returns>The scan result.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="content"/> is <c>null</c>.</exception>
    SecretScanResult Scan(byte[] content);
}