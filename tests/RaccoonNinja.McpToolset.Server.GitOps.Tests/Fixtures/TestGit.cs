using RaccoonNinja.McpToolset.Server.GitOps.Repo;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;

/// <summary>The git the tests run, found on the test process's <c>PATH</c> the way the server finds it at startup.</summary>
internal static class TestGit
{
    /// <summary>The absolute path of git.</summary>
    public static readonly string Executable = GitExecutableLocator.Find(Environment.GetEnvironmentVariable("PATH"));
}