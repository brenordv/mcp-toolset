using RaccoonNinja.McpToolset.Server.GitOps.Security;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Security;

public class EnvironmentBuilderTests
{
    [Fact]
    public void Build_DropsVariablesNotOnTheAllowlist()
    {
        // Arrange
        var parent = new Dictionary<string, string>
        {
            ["PATH"] = "/usr/bin",
            ["GIT_SSH_COMMAND"] = "evil",
            ["GIT_CONFIG_PARAMETERS"] = "evil",
            ["LD_PRELOAD"] = "evil",
            ["RANDOM_THING"] = "x",
        };

        // Act
        var env = EnvironmentBuilder.Build(parent);

        // Assert
        Assert.Equal("/usr/bin", env["PATH"]);
        Assert.False(env.ContainsKey("GIT_SSH_COMMAND"));
        Assert.False(env.ContainsKey("GIT_CONFIG_PARAMETERS"));
        Assert.False(env.ContainsKey("LD_PRELOAD"));
        Assert.False(env.ContainsKey("RANDOM_THING"));
    }

    [Fact]
    public void Build_AlwaysSetsGitNeutralizers()
    {
        // Act
        var env = EnvironmentBuilder.Build(new Dictionary<string, string>());

        // Assert
        Assert.Equal("0", env["GIT_TERMINAL_PROMPT"]);
        Assert.Equal("1", env["GIT_CONFIG_NOSYSTEM"]);
        Assert.Equal("0", env["GIT_OPTIONAL_LOCKS"]);
        Assert.Equal("1", env["GIT_NO_LAZY_FETCH"]);
    }

    [Fact]
    public void Build_ParentDisablesLazyFetch_KeepsLazyFetchDisabled()
    {
        // Arrange
        var parent = new Dictionary<string, string> { ["GIT_NO_LAZY_FETCH"] = "0" };

        // Act
        var env = EnvironmentBuilder.Build(parent);

        // Assert
        Assert.Equal("1", env["GIT_NO_LAZY_FETCH"]);
    }

    [Fact]
    public void Build_DoesNotSetLiteralPathspecs()
    {
        // Arrange
        var parent = new Dictionary<string, string> { ["GIT_LITERAL_PATHSPECS"] = "1" };

        // Act
        var env = EnvironmentBuilder.Build(parent);

        // Assert
        Assert.False(env.ContainsKey("GIT_LITERAL_PATHSPECS"));
    }
}