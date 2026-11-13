using System.ComponentModel;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.GitOps.Envelope;
using RaccoonNinja.McpToolset.Server.GitOps.Errors;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Logging;
using RaccoonNinja.McpToolset.Server.GitOps.Models;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tools;

/// <summary>
/// Returns the version the startup <c>git version</c> call read (<see cref="GitVersionProbe"/>). It starts no git process
/// and reads no repository, so it takes no arguments and its envelope has no <c>repo_root</c>.
/// </summary>
[McpServerToolType]
public sealed class GitVersionTool(ToolCommon common, GitVersionProbe probe)
{
    private const string NotInstalledMessage =
        "no git version was read when the server started: git was not found on PATH or could not be started";

    private const string TimeoutMessage = "no git version was read when the server started: git version did not exit in time";

    private const string CommandErrorMessage =
        "no git version was read when the server started: git version exited non-zero or printed an unexpected line";

    /// <summary>Return the version of the git the server runs, as the startup call read it.</summary>
    /// <returns>
    /// A one-item envelope holding a <see cref="GitVersionResult"/>, or a failure envelope carrying the startup call's
    /// error code.
    /// </returns>
    [McpServerTool(Name = "git_version")]
    [Description("Return the version of the git the server runs: { version }, the text after 'git version ' in git's own output (for example 2.56.0). Read once when the server started; takes no arguments and starts no git process. GitNotInstalled when no git was found on PATH at startup or it could not start; GitTimeout when git version did not exit within 5 seconds at startup; GitCommandError when it exited non-zero or printed no version line the server accepts.")]
    public Task<ResultEnvelope> InvokeAsync()
    {
        var ctx = common.MakeContext("git_version");
        return common.WrapAsync(ctx, null, () => probe.Version is null
            ? throw Unavailable()
            : Task.FromResult(ToolCommon.SingleSuccess(new GitVersionResult { Version = probe.Version }, null)));
    }

    /// <summary>Build the error for a startup call that read no version, carrying the code that call logged.</summary>
    /// <returns>The exception to throw inside <see cref="ToolCommon.WrapAsync"/>.</returns>
    private GitCheckException Unavailable() => probe.ErrorCode switch
    {
        ErrorCodes.GitNotInstalled => new GitNotInstalledException(
            NotInstalledMessage,
            new Dictionary<string, object> { ["executable"] = GitNotInstalledException.ExecutableDisplayName }),
        ErrorCodes.GitTimeout => new GitTimeoutException(TimeoutMessage),
        _ => new GitCommandException(
            CommandErrorMessage,
            probe.GitExitCode is { } exitCode ? new Dictionary<string, object> { [LogFields.GitExitCode] = exitCode } : null),
    };
}