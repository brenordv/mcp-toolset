using System.Text.RegularExpressions;
using RaccoonNinja.McpToolset.Files.Text;

namespace RaccoonNinja.McpToolset.Files.Security;

/// <summary>
/// The built-in content scanner. Decodes bytes through the shared <see cref="IEncodingDetector"/> (so a
/// UTF-16 secret is not a silent miss and binary payloads are skipped), then matches the decoded text
/// against a curated set of high-precision, backreference-free detectors compiled with
/// <see cref="RegexOptions.NonBacktracking"/> and a per-pattern timeout, so matching is linear-time. On a
/// detector timeout it fails closed (withholds the file). The optional aggressive layer adds
/// higher-false-positive detectors (JWTs, generic password assignments) and is off by default.
/// </summary>
public sealed class SecretContentScanner : ISecretContentScanner
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    // High-precision detectors, on by default. Each is single-line and backreference-free.
    private static readonly (string Id, string Pattern, bool IgnoreCase)[] DefaultPatterns =
    [
        ("pem_private_key", @"-----BEGIN (?:RSA |EC |OPENSSH |DSA |PGP |ENCRYPTED )?PRIVATE KEY-----", false),
        ("aws_access_key_id", @"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b", false),
        ("azure_storage_key", @"AccountKey=[A-Za-z0-9+/]{80,}={0,2}", false),
        ("azure_sas_token", @"\b(?:sig|SharedAccessKey)=[A-Za-z0-9%+/]{20,}", false),
        ("google_api_key", @"\bAIza[0-9A-Za-z_\-]{35}\b", false),
        ("gcp_service_account_key", "\"private_key\"\\s*:\\s*\"-----BEGIN", false),
        ("slack_token", @"\bxox[baprs]-[0-9A-Za-z-]{10,}\b", false),
        ("github_token", @"\bgh[pousr]_[0-9A-Za-z]{36,}\b", false),
        ("github_pat", @"\bgithub_pat_[0-9A-Za-z_]{22,}\b", false),
        ("google_oauth_client_secret", @"\bGOCSPX-[0-9A-Za-z_\-]{28}\b", false),
        ("stripe_secret_key", @"\b(?:sk|rk)_live_[0-9A-Za-z]{16,}\b", false),
        ("sendgrid_key", @"\bSG\.[0-9A-Za-z_\-]{22}\.[0-9A-Za-z_\-]{43}\b", false),
        ("url_userinfo_credentials", @"://[^/\s:@]+:[^/\s:@]+@", false),
    ];

    // Higher-false-positive detectors, off unless the aggressive layer is enabled.
    private static readonly (string Id, string Pattern, bool IgnoreCase)[] AggressivePatterns =
    [
        ("jwt", @"\beyJ[0-9A-Za-z_\-]+\.eyJ[0-9A-Za-z_\-]+\.[0-9A-Za-z_\-]+", false),
        ("generic_password_assignment", @"\b(?:password|passwd|pwd)\s*[=:]\s*\S{6,}", true),
    ];

    private readonly IEncodingDetector _detector;
    private readonly Detector[] _detectors;

    /// <summary>Create the scanner over an encoding detector.</summary>
    /// <param name="detector">The encoding detector used to decode content before scanning.</param>
    /// <param name="aggressive">When <c>true</c>, also enable the higher-false-positive detector layer.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="detector"/> is <c>null</c>.</exception>
    public SecretContentScanner(IEncodingDetector detector, bool aggressive = false)
    {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        var specs = aggressive ? [.. DefaultPatterns, .. AggressivePatterns] : DefaultPatterns;
        _detectors = specs.Select(spec => new Detector(spec.Id, Compile(spec.Pattern, spec.IgnoreCase))).ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> DetectorIds => _detectors.Select(detector => detector.Id).ToArray();

    /// <inheritdoc />
    public SecretScanResult Scan(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var detected = _detector.Detect(content);
        if (detected.IsBinary || detected.Encoding is null)
        {
            return SecretScanResult.None;
        }

        var text = detected.Encoding.GetString(content);
        foreach (var detector in _detectors)
        {
            try
            {
                if (detector.Regex.IsMatch(text))
                {
                    return new SecretScanResult(true, detector.Id);
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Fail closed: a scanner that cannot finish must not let a possible secret through.
                return new SecretScanResult(true, detector.Id);
            }
        }

        return SecretScanResult.None;
    }

    private static Regex Compile(string pattern, bool ignoreCase)
    {
        var options = RegexOptions.NonBacktracking | RegexOptions.CultureInvariant;
        if (ignoreCase)
        {
            options |= RegexOptions.IgnoreCase;
        }

        return new Regex(pattern, options, MatchTimeout);
    }

    private readonly record struct Detector(string Id, Regex Regex);
}

/// <summary>A content scanner that never matches, used when content scanning is disabled.</summary>
public sealed class NullSecretContentScanner : ISecretContentScanner
{
    /// <summary>The shared instance.</summary>
    public static NullSecretContentScanner Instance { get; } = new();

    private NullSecretContentScanner()
    {
    }

    /// <inheritdoc />
    public IReadOnlyList<string> DetectorIds => [];

    /// <inheritdoc />
    public SecretScanResult Scan(byte[] content) => SecretScanResult.None;
}