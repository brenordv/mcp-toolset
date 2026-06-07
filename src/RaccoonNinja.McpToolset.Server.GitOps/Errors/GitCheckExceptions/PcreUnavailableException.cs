namespace RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;

/// <summary>
/// Raised when a regex search (<c>fixedString=false</c>) is requested but the host's git was
/// built without PCRE2 (<c>USE_LIBPCRE</c>), so <c>git grep -P</c> is unavailable. The message
/// carries actionable remediation and never echoes git stderr.
/// </summary>
/// <remarks>
/// Surfacing this as a distinct, loud error is deliberate: a PCRE-less git exits non-zero rather
/// than matching nothing, so the alternative would be an opaque <see cref="GitCommandException"/>.
/// </remarks>
public sealed class PcreUnavailableException(IDictionary<string, object> detail = null)
    : GitCheckException(RemediationMessage, detail)
{
    /// <summary>Static, user-data-free remediation surfaced verbatim in the failure envelope.</summary>
    private const string RemediationMessage =
        "regex search is unavailable because this git build lacks PCRE2 support; " +
        "use fixed-string search (set fixedString=true) or POSIX character classes such as " +
        @"[[:space:]] or [[:alnum:]] instead of \s, \w or \d";

    /// <inheritdoc/>
    public override string Code => ErrorCodes.PcreUnavailable;
}