using RaccoonNinja.McpToolset.Server.GitOps.Security;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Security;

public class EnvironmentBuilderTests
{
    [Fact]
    public void Build_Drops_Variables_Not_On_The_Allowlist()
    {
        var parent = new Dictionary<string, string>
        {
            ["PATH"] = "/usr/bin",
            ["GIT_SSH_COMMAND"] = "evil",
            ["GIT_CONFIG_PARAMETERS"] = "evil",
            ["LD_PRELOAD"] = "evil",
            ["RANDOM_THING"] = "x",
        };
        var env = EnvironmentBuilder.Build(parent);

        Assert.Equal("/usr/bin", env["PATH"]);
        Assert.False(env.ContainsKey("GIT_SSH_COMMAND"));
        Assert.False(env.ContainsKey("GIT_CONFIG_PARAMETERS"));
        Assert.False(env.ContainsKey("LD_PRELOAD"));
        Assert.False(env.ContainsKey("RANDOM_THING"));
    }

    [Fact]
    public void Build_Always_Sets_Git_Neutralizers()
    {
        var env = EnvironmentBuilder.Build(new Dictionary<string, string>());
        Assert.Equal("0", env["GIT_TERMINAL_PROMPT"]);
        Assert.Equal("1", env["GIT_CONFIG_NOSYSTEM"]);
        Assert.Equal("0", env["GIT_OPTIONAL_LOCKS"]);
        Assert.Equal("1", env["GIT_LITERAL_PATHSPECS"]);
    }
}