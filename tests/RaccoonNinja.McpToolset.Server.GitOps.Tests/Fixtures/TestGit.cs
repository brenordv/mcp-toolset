using RaccoonNinja.McpToolset.Server.GitOps.Repo;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;

/// <summary>The git the tests run, found on the test process's <c>PATH</c> the way the server finds it at startup.</summary>
internal static class TestGit
{
    /// <summary>git's bare executable name, which no process start may receive.</summary>
    public const string BareName = "git";

    /// <summary>The detail key a <c>GitNotInstalled</c> error names the executable under.</summary>
    public const string NotInstalledDetailKey = "executable";

    /// <summary>How a <c>GitNotInstalled</c> error's detail names git: never by its path.</summary>
    public const string DisplayName = "git";

    /// <summary>The absolute path of git.</summary>
    public static readonly string Executable = GitExecutableLocator.Find(Environment.GetEnvironmentVariable("PATH"));
}