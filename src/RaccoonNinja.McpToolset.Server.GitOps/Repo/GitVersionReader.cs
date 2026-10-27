using System.Buffers;
using RaccoonNinja.McpToolset.Server.GitOps.Errors;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Logging;
using Serilog;

namespace RaccoonNinja.McpToolset.Server.GitOps.Repo;

/// <summary>
/// Runs <c>git version</c> once at startup and writes the result to the log, never to a tool. <c>version</c> is not a
/// <c>RUN_SETUP</c> builtin, so git discovers no repository and reads no repository config; the directory passed to
/// <c>-C</c> only satisfies <see cref="BootstrapGitRunner"/>.
/// </summary>
internal static class GitVersionReader
{
    /// <summary>The longest version text logged; longer output is treated as unparsable.</summary>
    internal const int MaxVersionLength = 64;

    /// <summary>git prints <c>git version %s</c> untranslated, so the prefix is the same in every locale.</summary>
    private const string Prefix = "git version ";

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);
    private static readonly string[] VersionArgs = ["version"];

    /// <summary>Every character a version token may hold, covering forms such as <c>2.47.1.windows.1</c> and <c>2.39.5 (Apple Git-154)</c>.</summary>
    private static readonly SearchValues<char> VersionChars =
        SearchValues.Create("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz._()+ -");

    /// <summary>Read the version from the first line of <c>git version</c>'s standard output.</summary>
    /// <param name="stdout">git's standard output.</param>
    /// <returns>
    /// The text after <c>git version </c>, trimmed, when it holds 1 to <see cref="MaxVersionLength"/> characters from
    /// the allowlist; otherwise <c>null</c>.
    /// </returns>
    public static string ParseVersion(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return null;
        }

        var line = stdout.Split('\n')[0].TrimEnd('\r');
        if (!line.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = line[Prefix.Length..].Trim();
        return rest.Length is >= 1 and <= MaxVersionLength && !rest.AsSpan().ContainsAnyExcept(VersionChars)
            ? rest
            : null;
    }

    /// <summary>
    /// Run <c>git version</c> and log <c>git_version</c>, or <c>git_version_unavailable</c> when git is missing, times
    /// out, exits non-zero, or prints a line that does not parse. Never throws for those cases.
    /// </summary>
    /// <param name="serverLogger">The server-scoped logger.</param>
    /// <param name="gitExecutable">The git executable to start.</param>
    /// <param name="cancellationToken">Cancels the call; a cancellation is rethrown, not logged.</param>
    /// <returns>A task that completes once the line is written.</returns>
    public static async Task LogAsync(ILogger serverLogger, string gitExecutable, CancellationToken cancellationToken)
    {
        try
        {
            var (stdout, exitCode) = await BootstrapGitRunner.RunAsync(
                gitExecutable,
                AppContext.BaseDirectory,
                VersionArgs,
                ProbeTimeout,
                onTimeout: null,
                cancellationToken);
            if (exitCode != 0)
            {
                ServerEventLog.GitVersionUnavailable(serverLogger, ErrorCodes.GitCommandError, exitCode);
                return;
            }

            var version = ParseVersion(stdout);
            if (version is null)
            {
                ServerEventLog.GitVersionUnavailable(serverLogger, ErrorCodes.GitCommandError, exitCode);
                return;
            }

            ServerEventLog.GitVersion(serverLogger, version);
        }
        catch (GitNotInstalledException)
        {
            ServerEventLog.GitVersionUnavailable(serverLogger, ErrorCodes.GitNotInstalled, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            ServerEventLog.GitVersionUnavailable(serverLogger, ErrorCodes.GitTimeout, null);
        }
    }
}