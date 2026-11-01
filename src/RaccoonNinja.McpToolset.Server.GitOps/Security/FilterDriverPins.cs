using System.Text;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;

namespace RaccoonNinja.McpToolset.Server.GitOps.Security;

/// <summary>
/// Pins the filter drivers a repository's config names. git runs a driver's <c>clean</c> or <c>process</c> command when
/// it hashes a worktree file whose <c>filter</c> attribute selects the driver, and both the driver (config) and the
/// attribute (<c>.gitattributes</c>, <c>.git/info/attributes</c>) can come with the repository. Driver names are
/// arbitrary, so the server lists them before each command that can hash a worktree file and overrides each one with
/// <c>-c</c>.
/// </summary>
public static class FilterDriverPins
{
    private const string KeyPrefix = "filter.";

    /// <summary>The <c>git config --get-regexp</c> pattern for every key in the <c>filter</c> section.</summary>
    private const string ListPattern = @"^filter\.";

    /// <summary>Throws on a byte sequence that is not UTF-8, where the default decoder would substitute U+FFFD.</summary>
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Whether a git process for <paramref name="subcommand"/> can run a filter driver's clean command: <c>status</c>
    /// hashes every stat-dirty worktree file, a <c>diff</c> against the working tree reads every file it compares, and
    /// <c>blame</c> without a revision reads the worktree file. Their object-only forms are included too and pay for one
    /// listing they do not need.
    /// </summary>
    /// <param name="subcommand">The git subcommand an intent runs.</param>
    /// <returns><c>true</c> for <c>status</c>, <c>diff</c>, and <c>blame</c>.</returns>
    public static bool AppliesTo(string subcommand) => subcommand is "status" or "diff" or "blame";

    /// <summary>
    /// The intent that lists every key in the <c>filter</c> section of the config git reads for
    /// <paramref name="repoRoot"/>, one NUL-terminated key per entry. The pattern sits in <see cref="GitIntent.Flags"/>,
    /// after <c>--get-regexp</c>, so the builder writes no <c>--end-of-options</c> in front of it; it is a server
    /// constant that does not start with <c>-</c>. git exits 1 when no key matches. <c>--get-regexp</c> is git-config's
    /// legacy mode, deprecated in git 2.46 in favor of <c>git config get --regexp</c>; it is kept because gits before
    /// 2.46 have no subcommand form.
    /// </summary>
    /// <param name="repoRoot">The resolved repository root.</param>
    /// <returns>The listing intent.</returns>
    public static GitIntent ListIntent(string repoRoot) => new()
    {
        Subcommand = "config",
        RepoRoot = repoRoot,
        Flags = ["-z", "--name-only", "--get-regexp", ListPattern],
    };

    /// <summary>
    /// Read the driver names from <paramref name="listing"/>: each key is <c>filter.&lt;driver&gt;.&lt;variable&gt;</c>,
    /// where the driver is everything between the first and the last dot and keeps its case. A key with no driver part
    /// (<c>filter.&lt;variable&gt;</c>) defines no driver and is skipped.
    /// </summary>
    /// <param name="listing">The standard output of <see cref="ListIntent"/>.</param>
    /// <returns>Each driver name once, in the order git listed it.</returns>
    /// <exception cref="GitCommandException">
    /// The listing is not valid UTF-8, or a name contains <c>=</c>; a <c>-c</c> argument could not name that driver.
    /// </exception>
    public static IReadOnlyList<string> ParseDriverNames(byte[] listing)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in Decode(listing).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var lastDot = key.LastIndexOf('.');
            if (!key.StartsWith(KeyPrefix, StringComparison.Ordinal) || lastDot < KeyPrefix.Length)
            {
                continue;
            }

            var driver = key[KeyPrefix.Length..lastDot];
            if (driver.Contains('='))
            {
                throw new GitCommandException("the repository's config names a filter driver whose name contains '=', which the server cannot override");
            }

            if (seen.Add(driver))
            {
                names.Add(driver);
            }
        }

        return names;
    }

    /// <summary>
    /// The <c>-c</c> pairs that leave <paramref name="driver"/> with nothing to run. git runs <c>clean</c> or
    /// <c>smudge</c> only when <c>process</c> is unset and the command is not empty, and <c>process</c> only when it is
    /// not empty; with <c>required</c> true and no filter result, git fails the command.
    /// </summary>
    /// <param name="driver">A name <see cref="ParseDriverNames"/> returned.</param>
    /// <returns>The <c>clean</c>, <c>smudge</c>, <c>process</c>, and <c>required</c> pairs, in that order.</returns>
    public static IReadOnlyList<(string Key, string Value)> PinsFor(string driver) =>
    [
        ($"{KeyPrefix}{driver}.clean", string.Empty),
        ($"{KeyPrefix}{driver}.smudge", string.Empty),
        ($"{KeyPrefix}{driver}.process", string.Empty),
        ($"{KeyPrefix}{driver}.required", "false"),
    ];

    /// <summary>Decode the listing as strict UTF-8, so a name is never passed to git changed.</summary>
    /// <param name="listing">The listing bytes.</param>
    /// <returns>The decoded listing.</returns>
    /// <exception cref="GitCommandException">The bytes are not valid UTF-8.</exception>
    private static string Decode(byte[] listing)
    {
        try
        {
            return StrictUtf8.GetString(listing);
        }
        catch (DecoderFallbackException)
        {
            throw new GitCommandException("the repository's config names a filter driver whose name is not valid UTF-8");
        }
    }
}