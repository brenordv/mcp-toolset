using RaccoonNinja.McpToolset.Server.GitOps.Repo;

namespace RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;

/// <summary>Raised when the configured <c>git</c> executable is not present or unreachable.</summary>
public sealed class GitNotInstalledException(string message, IDictionary<string, object> detail = null)
    : GitCheckException(message, detail)
{
    public override string Code => ErrorCodes.GitNotInstalled;

    /// <summary>
    /// Throw when <paramref name="executable"/> is not a fully qualified path, so a bare or relative name never reaches a
    /// process start. The server looks git up once at startup, so the message points at a restart.
    /// </summary>
    /// <param name="executable">The git executable about to be started; <c>null</c> when none was found on <c>PATH</c>.</param>
    /// <exception cref="GitNotInstalledException"><paramref name="executable"/> is null, blank, or not fully qualified.</exception>
    public static void ThrowIfNotFullyQualified(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable) || !Path.IsPathFullyQualified(executable))
        {
            throw new GitNotInstalledException(
                "git executable not found on PATH when the server started",
                new Dictionary<string, object> { ["executable"] = GitExecutableLocator.DisplayName });
        }
    }
}